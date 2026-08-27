using System.Collections.Concurrent;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Mizzle.Compile;
using Mizzle.Fluent;
using Mizzle.Ir;
using Mizzle.Schema;

namespace Mizzle.SqlServer;

public sealed class SqlDb : IQueryExecutor
{
    private readonly SqlDataSource _dataSource;
    private readonly MizzleOptions _options;
    private readonly SqlServerEmitter _emitter = new();
    private readonly ConcurrentDictionary<Query, string> _sqlCache = new();
    private readonly AsyncLocal<SqlTx?> _ambient = new();

    public SqlDb(SqlDataSource dataSource, MizzleOptions? options = null)
    {
        _dataSource = dataSource;
        _options = options ?? new MizzleOptions();
    }

    public SelectBuilder Select(params IColumn[] columns)
        => new SelectBuilder(this).Select(columns);

    public UpdateBuilder Update(ITable table)
        => new(table, this);

    public InsertBuilder InsertInto(ITable table)
        => new(table, this);

    public DeleteBuilder DeleteFrom(ITable table)
        => new(table, this);

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
        var tx = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);
        var scope = new SqlTx(this, conn, tx, depth: 0);
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
        if (_ambient.Value is { } ambient)
        {
            await using var cmd = CreateCommand(ambient.Connection, sql, values, overlay, ambient.DbTransaction);
            return await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var outer = CreateCommand(conn, sql, values, overlay, transaction: null);
        return await outer.ExecuteNonQueryAsync(cancellationToken);
    }

    public async IAsyncEnumerable<T> StreamAsync<T>(
        Query query,
        Func<DbDataReader, T> map,
        QueryOptions? overlay,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureCompiledQuery();
        var (sql, values) = Compile(query);
        if (_ambient.Value is { } ambient)
        {
            await using var cmd = CreateCommand(ambient.Connection, sql, values, overlay, ambient.DbTransaction);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                yield return map(reader);
            }

            yield break;
        }

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var outer = CreateCommand(conn, sql, values, overlay, transaction: null);
        await using var outerReader = await outer.ExecuteReaderAsync(cancellationToken);
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
        if (_ambient.Value is { } ambient)
        {
            await using var cmd = CreateCommand(ambient.Connection, sql, values, overlay, ambient.DbTransaction);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(map(reader));
            }

            return rows;
        }

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var outer = CreateCommand(conn, sql, values, overlay, transaction: null);
        await using var outerReader = await outer.ExecuteReaderAsync(cancellationToken);
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

    private SqlCommand CreateCommand(
        SqlConnection connection,
        string sql,
        IReadOnlyList<object?> values,
        QueryOptions? overlay,
        SqlTransaction? transaction)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = transaction;
        var timeout = overlay?.CommandTimeout ?? _options.CommandTimeout;
        cmd.CommandTimeout = (int)Math.Ceiling(timeout.TotalSeconds);
        for (var i = 0; i < values.Count; i++)
        {
            cmd.Parameters.Add(new SqlParameter($"@p{i}", values[i] ?? DBNull.Value));
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

    private sealed class SqlTx : IMizzleTransaction
    {
        private readonly SqlDb _db;
        private int _depth;

        public SqlTx(SqlDb db, SqlConnection connection, SqlTransaction transaction, int depth)
        {
            _db = db;
            Connection = connection;
            DbTransaction = transaction;
            _depth = depth;
        }

        public SqlConnection Connection { get; }
        public SqlTransaction DbTransaction { get; }

        public async Task<T> AtSavepoint<T>(Func<IMizzleTransaction, Task<T>> body, CancellationToken cancellationToken)
        {
            _depth++;
            var name = $"mizzle_sp_{_depth}";
            await using (var cmd = Connection.CreateCommand())
            {
                cmd.Transaction = DbTransaction;
                cmd.CommandText = $"SAVE TRANSACTION {name}";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            try
            {
                return await body(this);
            }
            catch
            {
                await using var rollback = Connection.CreateCommand();
                rollback.Transaction = DbTransaction;
                rollback.CommandText = $"ROLLBACK TRANSACTION {name}";
                await rollback.ExecuteNonQueryAsync(cancellationToken);
                throw;
            }
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            Query query,
            Func<DbDataReader, T> map,
            QueryOptions? overlay,
            CancellationToken cancellationToken)
            => _db.QueryAsync(query, map, overlay, cancellationToken);

        public Task<int> ExecuteAsync(
            Query query,
            QueryOptions? overlay,
            CancellationToken cancellationToken)
            => _db.ExecuteAsync(query, overlay, cancellationToken);

        public IAsyncEnumerable<T> StreamAsync<T>(
            Query query,
            Func<DbDataReader, T> map,
            QueryOptions? overlay,
            CancellationToken cancellationToken)
            => _db.StreamAsync(query, map, overlay, cancellationToken);

        public Task<IReadOnlyList<T>> QueryPrecompiledAsync<T>(
            string sql,
            Query query,
            Func<DbDataReader, T> map,
            QueryOptions? overlay,
            CancellationToken cancellationToken)
            => _db.QueryPrecompiledAsync(sql, query, map, overlay, cancellationToken);

        public Task LockAsync(string resource, CancellationToken cancellationToken = default)
            => SqlLock.AcquireAsync(_db, resource, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
