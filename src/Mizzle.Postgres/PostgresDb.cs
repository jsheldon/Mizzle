using System.Collections.Concurrent;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Mizzle.Compile;
using Mizzle.Fluent;
using Mizzle.Ir;
using Mizzle.Schema;
using Npgsql;

namespace Mizzle.Postgres;

public sealed class PostgresDb : IQueryExecutor
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly MizzleOptions _options;
    private readonly PgEmitter _emitter = new();
    private readonly ConcurrentDictionary<Query, string> _sqlCache = new();
    private readonly AsyncLocal<PostgresTransaction?> _ambientTransaction = new();

    public PostgresDb(NpgsqlDataSource dataSource, MizzleOptions? options = null)
    {
        _dataSource = dataSource;
        _options = options ?? new MizzleOptions();
    }

    /// <summary>
    ///     Starts a select. Columns convert implicitly; expressions are aliased with
    ///     <c>Sql.As(...)</c>, and the two can be mixed.
    /// </summary>
    public SelectBuilder Select(params SelectItem[] items)
        => new SelectBuilder(this).Select(items);

    public UpdateBuilder Update(ITable table)
        => new(table, this);

    public InsertBuilder InsertInto(ITable table)
        => new(table, this);

    public DeleteBuilder DeleteFrom(ITable table)
        => new(table, this);

    public Task Transaction(Func<IMizzleTransaction, Task> body, CancellationToken cancellationToken = default)
        => Transaction(async transaction =>
        {
            await body(transaction);
            return 0;
        }, cancellationToken);

    public async Task<T> Transaction<T>(
        Func<IMizzleTransaction, Task<T>> body,
        CancellationToken cancellationToken = default)
    {
        if (_ambientTransaction.Value is { } current)
        {
            return await current.AtSavepoint(body, cancellationToken);
        }

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var scope = new PostgresTransaction(this, connection, transaction, depth: 0);
        _ambientTransaction.Value = scope;
        try
        {
            var result = await body(scope);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            _ambientTransaction.Value = null;
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        Query query,
        Func<DbDataReader, T> map,
        QueryOptions? overlay,
        CancellationToken cancellationToken)
    {
        EnsureCompiledQuery();
        var rows = new List<T>();
        await foreach (var row in StreamAsync(query, map, overlay, cancellationToken))
        {
            rows.Add(row);
        }

        return rows;
    }

    public async Task<int> ExecuteAsync(
        Query query,
        QueryOptions? overlay,
        CancellationToken cancellationToken)
    {
        var (sql, values) = Compile(query);
        if (_ambientTransaction.Value is { } ambient)
        {
            await using var command = CreateCommand(ambient.Connection, sql, values, overlay, ambient.DbTransaction);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var standaloneCommand = CreateCommand(connection, sql, values, overlay, transaction: null);
        return await standaloneCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public async IAsyncEnumerable<T> StreamAsync<T>(
        Query query,
        Func<DbDataReader, T> map,
        QueryOptions? overlay,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureCompiledQuery();
        var (sql, values) = Compile(query);
        if (_ambientTransaction.Value is { } ambient)
        {
            await using var command = CreateCommand(ambient.Connection, sql, values, overlay, ambient.DbTransaction);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                yield return map(reader);
            }

            yield break;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var standaloneCommand = CreateCommand(connection, sql, values, overlay, transaction: null);
        await using var outerReader = await standaloneCommand.ExecuteReaderAsync(cancellationToken);
        while (await outerReader.ReadAsync(cancellationToken))
        {
            yield return map(outerReader);
        }
    }

    public async Task<IReadOnlyList<T>> QueryPrecompiledAsync<T>(
        string sql,
        Query query,
        Func<DbDataReader, T> map,
        QueryOptions? overlay,
        CancellationToken cancellationToken)
    {
        var (_, values) = Parameterizer.Run(query);
        var rows = new List<T>();
        if (_ambientTransaction.Value is { } ambient)
        {
            await using var command = CreateCommand(ambient.Connection, sql, values, overlay, ambient.DbTransaction);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(map(reader));
            }

            return rows;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var standaloneCommand = CreateCommand(connection, sql, values, overlay, transaction: null);
        await using var outerReader = await standaloneCommand.ExecuteReaderAsync(cancellationToken);
        while (await outerReader.ReadAsync(cancellationToken))
        {
            rows.Add(map(outerReader));
        }

        return rows;
    }

    private (string Sql, IReadOnlyList<object?> Values) Compile(Query query)
    {
        if (query is LockQuery lockQuery)
        {
            return (_sqlCache.GetOrAdd(query, q => _emitter.Emit(q, []).Sql), [lockQuery.Resource]);
        }

        var (canonical, values) = Parameterizer.Run(query);
        var sql = _sqlCache.GetOrAdd(canonical, q => _emitter.Emit(q, values).Sql);
        return (sql, values);
    }

    private NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        string sql,
        IReadOnlyList<object?> values,
        QueryOptions? overlay,
        NpgsqlTransaction? transaction)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        var timeout = overlay?.CommandTimeout ?? _options.CommandTimeout;
        command.CommandTimeout = (int)Math.Ceiling(timeout.TotalSeconds);
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value ?? DBNull.Value });
        }

        return command;
    }

    private void EnsureCompiledQuery()
    {
        if (_options.AssertCompiledQueries)
        {
            throw new InvalidOperationException("Query was not interceptable");
        }
    }

    private sealed class PostgresTransaction : IMizzleTransaction
    {
        private readonly PostgresDb _database;
        private int _savepointSequence;

        public PostgresTransaction(PostgresDb db, NpgsqlConnection connection, NpgsqlTransaction transaction, int depth)
        {
            _database = db;
            Connection = connection;
            DbTransaction = transaction;
            _savepointSequence = depth;
        }

        public NpgsqlConnection Connection { get; }
        public NpgsqlTransaction DbTransaction { get; }

        public async Task<T> AtSavepoint<T>(Func<IMizzleTransaction, Task<T>> body, CancellationToken cancellationToken)
        {
            _savepointSequence++;
            var name = $"mizzle_sp_{_savepointSequence}";
            await using (var command = Connection.CreateCommand())
            {
                command.Transaction = DbTransaction;
                command.CommandText = $"SAVEPOINT {name}";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            try
            {
                var result = await body(this);
                await using (var release = Connection.CreateCommand())
                {
                    release.Transaction = DbTransaction;
                    release.CommandText = $"RELEASE SAVEPOINT {name}";
                    await release.ExecuteNonQueryAsync(cancellationToken);
                }

                return result;
            }
            catch
            {
                await using var rollback = Connection.CreateCommand();
                rollback.Transaction = DbTransaction;
                rollback.CommandText = $"ROLLBACK TO SAVEPOINT {name}";
                await rollback.ExecuteNonQueryAsync(cancellationToken);
                throw;
            }
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            Query query,
            Func<DbDataReader, T> map,
            QueryOptions? overlay,
            CancellationToken cancellationToken)
            => _database.QueryAsync(query, map, overlay, cancellationToken);

        public Task<int> ExecuteAsync(
            Query query,
            QueryOptions? overlay,
            CancellationToken cancellationToken)
            => _database.ExecuteAsync(query, overlay, cancellationToken);

        public IAsyncEnumerable<T> StreamAsync<T>(
            Query query,
            Func<DbDataReader, T> map,
            QueryOptions? overlay,
            CancellationToken cancellationToken)
            => _database.StreamAsync(query, map, overlay, cancellationToken);

        public Task<IReadOnlyList<T>> QueryPrecompiledAsync<T>(
            string sql,
            Query query,
            Func<DbDataReader, T> map,
            QueryOptions? overlay,
            CancellationToken cancellationToken)
            => _database.QueryPrecompiledAsync(sql, query, map, overlay, cancellationToken);

        public Task LockAsync(string resource, CancellationToken cancellationToken = default)
            => PgLock.AcquireAsync(_database, resource, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
