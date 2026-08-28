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

    public DeleteBuilder Returning(params IColumn[] columns)
        => Copy(
            returning: [..columns.Select(c => new SelectItem(c.ToRef(), c.ProjectionName))],
            returningColumns: [..columns.Select(RuntimeProjectionColumn.From)]);

    public DeleteBuilder With(CteClause cte) => Copy(with: [.._with, cte]);

    public DeleteBuilder WithRecursive(CteClause cte)
        => Copy(with: [.._with, cte], recursiveWith: true);

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


    public Task<IReadOnlyList<T>> ToListAsync<T>(CancellationToken cancellationToken = default)
    {
        EnsureReturningProjection();
        return ToListAsync(reader => RuntimeProjectionMapper.Read<T>(_returningColumns, reader), cancellationToken);
    }

    public async Task<T> FirstAsync<T>(CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync<T>(cancellationToken);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("Sequence contains no elements.");
        }

        return rows[0];
    }

    public async Task<T?> FirstOrDefaultAsync<T>(CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync<T>(cancellationToken);
        return rows.Count == 0 ? default : rows[0];
    }

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
