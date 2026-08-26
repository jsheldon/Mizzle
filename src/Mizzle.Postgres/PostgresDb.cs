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
    private readonly AsyncLocal<PostgresTx?> _ambient = new();

    public PostgresDb(NpgsqlDataSource dataSource, MizzleOptions? options = null)
    {
        _dataSource = dataSource;
        _options = options ?? new MizzleOptions();
    }

    public SelectBuilder Select(params IColumn[] columns)
        => new SelectBuilder(new ParamBag(), this).Select(columns);

    public UpdateBuilder Update(ITable table)
        => new UpdateBuilder(table, new ParamBag(), this);

    public InsertBuilder InsertInto(ITable table)
        => new(table, new ParamBag(), this);

    public DeleteBuilder DeleteFrom(ITable table)
        => new(table, new ParamBag(), this);

    public Task Transaction(Func<IMizzleTransaction, Task> body, CancellationToken cancellationToken = default)
        => Transaction(async tx =>
        {
            await body(tx);
            return 0;
        }, cancellationToken);

    public async Task<T> Transaction<T>(
        Func<IMizzleTransaction, Task<T>> body,
        CancellationToken cancellationToken = default)
    {
        if (_ambient.Value is { } current)
        {
            return await current.AtSavepoint(body, cancellationToken);
        }

        var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        var tx = await conn.BeginTransactionAsync(cancellationToken);
        var scope = new PostgresTx(this, conn, tx, depth: 0);
        _ambient.Value = scope;
        try
        {
            var result = await body(scope);
            await tx.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            _ambient.Value = null;
            await tx.DisposeAsync();
            await conn.DisposeAsync();
        }
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        Query query,
        ParamBag bag,
        Func<DbDataReader, T> map,
        QueryOptions? overlay,
        CancellationToken cancellationToken)
    {
        EnsureCompiledQuery();
        var rows = new List<T>();
        await foreach (var row in StreamAsync(query, bag, map, overlay, cancellationToken))
        {
            rows.Add(row);
        }

        return rows;
    }

    public async Task<int> ExecuteAsync(
        Query query,
        ParamBag bag,
        QueryOptions? overlay,
        CancellationToken cancellationToken)
    {
        var sql = Compile(query, bag);
        if (_ambient.Value is { } ambient)
        {
            await using var cmd = CreateCommand(ambient.Connection, sql, bag, overlay, ambient.DbTransaction);
            return await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var outer = CreateCommand(conn, sql, bag, overlay, transaction: null);
        return await outer.ExecuteNonQueryAsync(cancellationToken);
    }

    public async IAsyncEnumerable<T> StreamAsync<T>(
        Query query,
        ParamBag bag,
        Func<DbDataReader, T> map,
        QueryOptions? overlay,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureCompiledQuery();
        var sql = Compile(query, bag);
        if (_ambient.Value is { } ambient)
        {
            await using var cmd = CreateCommand(ambient.Connection, sql, bag, overlay, ambient.DbTransaction);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                yield return map(reader);
            }

            yield break;
        }

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var outer = CreateCommand(conn, sql, bag, overlay, transaction: null);
        await using var outerReader = await outer.ExecuteReaderAsync(cancellationToken);
        while (await outerReader.ReadAsync(cancellationToken))
        {
            yield return map(outerReader);
        }
    }

    public async Task<IReadOnlyList<T>> QueryPrecompiledAsync<T>(
        string sql,
        ParamBag bag,
        Func<DbDataReader, T> map,
        QueryOptions? overlay,
        CancellationToken cancellationToken)
    {
        var rows = new List<T>();
        if (_ambient.Value is { } ambient)
        {
            await using var cmd = CreateCommand(ambient.Connection, sql, bag, overlay, ambient.DbTransaction);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(map(reader));
            }

            return rows;
        }

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var outer = CreateCommand(conn, sql, bag, overlay, transaction: null);
        await using var outerReader = await outer.ExecuteReaderAsync(cancellationToken);
        while (await outerReader.ReadAsync(cancellationToken))
        {
            rows.Add(map(outerReader));
        }

        return rows;
    }

    private string Compile(Query query, ParamBag bag)
        => _sqlCache.GetOrAdd(query, q => _emitter.Emit(q, bag).Sql);

    private NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        string sql,
        ParamBag bag,
        QueryOptions? overlay,
        NpgsqlTransaction? transaction)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = transaction;
        var timeout = overlay?.CommandTimeout ?? _options.CommandTimeout;
        cmd.CommandTimeout = (int)Math.Ceiling(timeout.TotalSeconds);
        foreach (var value in bag.Values)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = value ?? DBNull.Value });
        }

        return cmd;
    }

    private void EnsureCompiledQuery()
    {
        if (_options.AssertCompiledQueries)
        {
            throw new InvalidOperationException("Query was not interceptable");
        }
    }

    private sealed class PostgresTx : IMizzleTransaction
    {
        private readonly PostgresDb _db;
        private int _depth;

        public PostgresTx(PostgresDb db, NpgsqlConnection connection, NpgsqlTransaction transaction, int depth)
        {
            _db = db;
            Connection = connection;
            DbTransaction = transaction;
            _depth = depth;
        }

        public NpgsqlConnection Connection { get; }
        public NpgsqlTransaction DbTransaction { get; }

        public async Task<T> AtSavepoint<T>(Func<IMizzleTransaction, Task<T>> body, CancellationToken cancellationToken)
        {
            _depth++;
            var name = $"mizzle_sp_{_depth}";
            await using (var cmd = Connection.CreateCommand())
            {
                cmd.Transaction = DbTransaction;
                cmd.CommandText = $"SAVEPOINT {name}";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
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
            ParamBag bag,
            Func<DbDataReader, T> map,
            QueryOptions? overlay,
            CancellationToken cancellationToken)
            => _db.QueryAsync(query, bag, map, overlay, cancellationToken);

        public Task<int> ExecuteAsync(
            Query query,
            ParamBag bag,
            QueryOptions? overlay,
            CancellationToken cancellationToken)
            => _db.ExecuteAsync(query, bag, overlay, cancellationToken);

        public IAsyncEnumerable<T> StreamAsync<T>(
            Query query,
            ParamBag bag,
            Func<DbDataReader, T> map,
            QueryOptions? overlay,
            CancellationToken cancellationToken)
            => _db.StreamAsync(query, bag, map, overlay, cancellationToken);

        public Task<IReadOnlyList<T>> QueryPrecompiledAsync<T>(
            string sql,
            ParamBag bag,
            Func<DbDataReader, T> map,
            QueryOptions? overlay,
            CancellationToken cancellationToken)
            => _db.QueryPrecompiledAsync(sql, bag, map, overlay, cancellationToken);

        public Task LockAsync(string resource, CancellationToken cancellationToken = default)
            => PgLock.AcquireAsync(_db, resource, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
