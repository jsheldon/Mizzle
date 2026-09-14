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
    private readonly AsyncLocal<SqlServerTransaction?> _ambientTransaction = new();

    public SqlDb(SqlDataSource dataSource, MizzleOptions? options = null)
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

    /// <summary>
    ///     Checks whether <paramref name="column"/> exists on <paramref name="table"/> in the
    ///     connected database's live schema, via the ANSI-standard <c>information_schema.columns</c>
    ///     view -- useful when different environments (or tenants sharing this connection string)
    ///     can have drifted schemas and a query needs to branch on whether a column is present
    ///     before referencing it. An ordinary runtime query, not baked: schema checks like this are
    ///     rare and typically cached by the caller, not a per-request hot path.
    /// </summary>
    /// <param name="table">The table to check. Its own <see cref="ITable.Name"/>/<see cref="ITable.Schema"/>
    ///     are used, not its alias, so this gives the right answer even if the instance was
    ///     constructed with <c>WithAlias(...)</c>.</param>
    /// <param name="column">The column to check for, e.g. <c>person.PersonId</c>.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    public async Task<bool> ColumnExistsAsync(ITable table, IColumn column, CancellationToken cancellationToken = default)
    {
        var isc = new InformationSchemaColumns();
        var found = await Select(isc.ColumnName)
            .From(isc)
            .Where(isc.TableName.Eq(table.Name))
            .Where(isc.ColumnName.Eq(column.Name))
            .WhereIf(table.Schema is not null, isc.TableSchema.Eq(table.Schema!))
            .FirstOrDefaultAsync(static _ => true, cancellationToken);

        return found;
    }

    /// <summary>
    ///     Checks several table/column pairs in a single round trip, for callers that probe
    ///     multiple schema-shape flags at once (e.g. tenants whose live DB schema can drift, where
    ///     probing every known flag per-request would otherwise mean one round trip per flag).
    /// </summary>
    /// <param name="checks">The table/column pairs to check. Each table's own
    ///     <see cref="ITable.Name"/>/<see cref="ITable.Schema"/> are used, not its alias.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    /// <returns>
    ///     The subset of <paramref name="checks"/> that exist, as (table name, column name) pairs.
    ///     Check membership with the same names you passed in, e.g.
    ///     <c>result.Contains((table.Name, column.Name))</c>.
    /// </returns>
    public async Task<IReadOnlySet<(string Table, string Column)>> ColumnsExistAsync(
        IReadOnlyList<(ITable Table, IColumn Column)> checks,
        CancellationToken cancellationToken = default)
    {
        if (checks.Count == 0)
            return new HashSet<(string, string)>();

        var isc = new InformationSchemaColumns();
        var clauses = checks.Select(check =>
        {
            Expr clause = Sql.And(isc.TableName.Eq(check.Table.Name), isc.ColumnName.Eq(check.Column.Name));
            return check.Table.Schema is not null
                ? Sql.And(clause, isc.TableSchema.Eq(check.Table.Schema!))
                : clause;
        }).ToArray();

        var predicate = clauses.Length == 1 ? clauses[0] : Sql.Or(clauses);
        var rows = await Select(isc.TableName, isc.ColumnName)
            .From(isc)
            .Where(predicate)
            .ToListAsync(static r => (r.GetString(0), r.GetString(1)), cancellationToken);

        return rows.ToHashSet();
    }

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
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var scope = new SqlServerTransaction(this, connection, transaction, depth: 0);
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

    private SqlCommand CreateCommand(
        SqlConnection connection,
        string sql,
        IReadOnlyList<object?> values,
        QueryOptions? overlay,
        SqlTransaction? transaction)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        var timeout = overlay?.CommandTimeout ?? _options.CommandTimeout;
        command.CommandTimeout = (int)Math.Ceiling(timeout.TotalSeconds);
        for (var i = 0; i < values.Count; i++)
        {
            command.Parameters.Add(new SqlParameter($"@p{i}", values[i] ?? DBNull.Value));
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

    private sealed class SqlServerTransaction : IMizzleTransaction
    {
        private readonly SqlDb _database;
        private int _savepointSequence;

        public SqlServerTransaction(SqlDb db, SqlConnection connection, SqlTransaction transaction, int depth)
        {
            _database = db;
            Connection = connection;
            DbTransaction = transaction;
            _savepointSequence = depth;
        }

        public SqlConnection Connection { get; }
        public SqlTransaction DbTransaction { get; }

        public async Task<T> AtSavepoint<T>(Func<IMizzleTransaction, Task<T>> body, CancellationToken cancellationToken)
        {
            _savepointSequence++;
            var name = $"mizzle_sp_{_savepointSequence}";
            await using (var command = Connection.CreateCommand())
            {
                command.Transaction = DbTransaction;
                command.CommandText = $"SAVE TRANSACTION {name}";
                await command.ExecuteNonQueryAsync(cancellationToken);
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

        public Task LockAsync(string resource, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            => SqlLock.AcquireAsync(_database, resource, timeout, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
