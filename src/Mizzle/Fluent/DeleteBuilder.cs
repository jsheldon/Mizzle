using System.Data.Common;
using Mizzle.Ir;
using Mizzle.Schema;

namespace Mizzle.Fluent;

public sealed class DeleteBuilder
{
    private readonly ITable _table;
    private readonly IQueryExecutor? _executor;
    private readonly Expr? _where;
    private readonly EquatableList<SelectItem> _returning;
    private readonly EquatableList<RuntimeProjectionColumn> _returningColumns;
    private readonly EquatableList<CteClause> _with;
    private readonly bool _recursiveWith;
    private readonly int? _expect;

    public DeleteBuilder(ITable table, IQueryExecutor? executor = null, QueryOptions? overlay = null)
        : this(table, executor, overlay, null, [], [], [], false, null)
    {
    }

    private DeleteBuilder(
        ITable table,
        IQueryExecutor? executor,
        QueryOptions? overlay,
        Expr? where,
        EquatableList<SelectItem> returning,
        EquatableList<RuntimeProjectionColumn> returningColumns,
        EquatableList<CteClause> with,
        bool recursiveWith,
        int? expect)
    {
        _table = table;
        _executor = executor;
        Overlay = overlay;
        _where = where;
        _returning = returning;
        _returningColumns = returningColumns;
        _with = with;
        _recursiveWith = recursiveWith;
        _expect = expect;
    }

    public QueryOptions? Overlay { get; }

    public DeleteBuilder Where(Expr expr)
        => Copy(where: _where is null ? expr : Sql.And(_where, expr));

    public DeleteBuilder Where(IColumn column, object? value)
        => Where(new BinaryExpr(BinaryOp.Eq, column.ToRef(), column.Bind(value)));

    /// <summary>
    ///     The columns to return from the affected rows, read back through the typed
    ///     terminators. <c>As(...)</c> works here as it does in a select list.
    /// </summary>
    public DeleteBuilder Returning(params IColumn[] columns)
        => Copy(
            returning: [..columns.Select(c => new SelectItem(c.ToRef(), c.ProjectionName))],
            returningColumns: [..columns.Select(RuntimeProjectionColumn.From)]);

    /// <summary>Prefixes the statement with a common table expression.</summary>
    public DeleteBuilder With(CteClause cte) => Copy(with: [.._with, cte]);

    /// <summary>Prefixes the statement with a <c>WITH RECURSIVE</c> common table expression.</summary>
    public DeleteBuilder WithRecursive(CteClause cte)
        => Copy(with: [.._with, cte], recursiveWith: true);

    /// <summary>
    ///     The row count this statement must affect. Anything else throws
    ///     <see cref="ConcurrencyException"/> rather than silently succeeding.
    /// </summary>
    public DeleteBuilder Expect(int affectedRows) => Copy(expect: affectedRows);

    public DeleteBuilder Timeout(TimeSpan timeout) => Copy(overlay: new QueryOptions(timeout));

    public DeleteQuery Build() => new(_table.ToFrom(), _where, _returning, _with, _recursiveWith);

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var affected = await Executor().ExecuteAsync(Build(), Overlay, cancellationToken);
        if (_expect is int expected && affected != expected)
        {
            throw new ConcurrencyException(expected, affected);
        }

        return affected;
    }

    public Task<IReadOnlyList<T>> ToListAsync<T>(
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
        => Executor().QueryAsync(Build(), map, Overlay, cancellationToken);


    /// <summary>Runs the query and maps every row to <typeparamref name="T"/>.</summary>
    public Task<IReadOnlyList<T>> ToListAsync<T>(CancellationToken cancellationToken = default)
    {
        EnsureReturningProjection();
        return ToListAsync(reader => RuntimeProjectionMapper.Read<T>(_returningColumns, reader), cancellationToken);
    }

    /// <summary>Runs the query and returns the first row.</summary>
    /// <exception cref="InvalidOperationException">No rows were returned.</exception>
    public async Task<T> FirstAsync<T>(CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync<T>(cancellationToken);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("Sequence contains no elements.");
        }

        return rows[0];
    }

    /// <summary>Runs the query and returns the first row, or <c>default</c> if there are none.</summary>
    public async Task<T?> FirstOrDefaultAsync<T>(CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync<T>(cancellationToken);
        return rows.Count == 0 ? default : rows[0];
    }

    /// <summary>Runs the query and returns the only row.</summary>
    /// <exception cref="InvalidOperationException">Zero or more than one row was returned.</exception>
    public async Task<T> SingleAsync<T>(CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync<T>(cancellationToken);
        return rows.Count switch
        {
            0 => throw new InvalidOperationException("Sequence contains no elements."),
            1 => rows[0],
            _ => throw new InvalidOperationException("Sequence contains more than one element.")
        };
    }

    /// <summary>Runs the query and returns the only row, or <c>default</c> if there are none.</summary>
    /// <exception cref="InvalidOperationException">More than one row was returned.</exception>
    public async Task<T?> SingleOrDefaultAsync<T>(CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync<T>(cancellationToken);
        return rows.Count switch
        {
            0 => default,
            1 => rows[0],
            _ => throw new InvalidOperationException("Sequence contains more than one element.")
        };
    }

    private IQueryExecutor Executor()
        => _executor ?? throw new InvalidOperationException("This query is not bound to a database.");

    private void EnsureReturningProjection()
    {
        if (_returningColumns.Count == 0)
        {
            throw new InvalidOperationException("Typed delete projection requires Returning(...).");
        }
    }

    private DeleteBuilder Copy(
        Expr? where = null,
        EquatableList<SelectItem>? returning = null,
        EquatableList<RuntimeProjectionColumn>? returningColumns = null,
        EquatableList<CteClause>? with = null,
        bool? recursiveWith = null,
        int? expect = null,
        QueryOptions? overlay = null)
        => new(
            _table,
            _executor,
            overlay ?? Overlay,
            where ?? _where,
            returning ?? _returning,
            returningColumns ?? _returningColumns,
            with ?? _with,
            recursiveWith ?? _recursiveWith,
            expect ?? _expect);
}
