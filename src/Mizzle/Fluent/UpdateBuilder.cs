using Mizzle.Ir;
using Mizzle.Schema;

namespace Mizzle.Fluent;

public sealed class UpdateBuilder
{
    private readonly ITable _table;
    private readonly IQueryExecutor? _executor;
    private readonly EquatableList<(string Column, Expr Value)> _set;
    private readonly Expr? _where;
    private readonly EquatableList<SelectItem> _returning;
    private readonly int? _expect;

    public UpdateBuilder(ITable table, IQueryExecutor? executor = null, QueryOptions? overlay = null)
        : this(table, executor, overlay, [], null, [], null)
    {
    }

    private UpdateBuilder(
        ITable table,
        IQueryExecutor? executor,
        QueryOptions? overlay,
        EquatableList<(string Column, Expr Value)> set,
        Expr? where,
        EquatableList<SelectItem> returning,
        int? expect)
    {
        _table = table;
        _executor = executor;
        Overlay = overlay;
        _set = set;
        _where = where;
        _returning = returning;
        _expect = expect;
    }

    public QueryOptions? Overlay { get; }

    public UpdateBuilder Set(IColumn column, object? value)
        => Copy(set: [.._set, (column.Name, (Expr)column.Bind(value))]);

    public UpdateBuilder Where(Expr expr)
        => Copy(where: _where is null ? expr : Sql.And(_where, expr));

    public UpdateBuilder Where(IColumn column, object? value)
        => Where(new BinaryExpr(BinaryOp.Eq, column.ToRef(), column.Bind(value)));

    public UpdateBuilder Returning(params IColumn[] columns)
        => Copy(returning: [..columns.Select(c => new SelectItem(c.ToRef(), null))]);

    public UpdateBuilder Expect(int affectedRows) => Copy(expect: affectedRows);

    public UpdateBuilder Timeout(TimeSpan timeout) => Copy(overlay: new QueryOptions(timeout));

    public UpdateQuery Build()
    {
        EnsureVersionInWhere();
        if (_set.Count == 0)
        {
            throw new InvalidOperationException("SET is required.");
        }

        return new UpdateQuery(_table.ToFrom(), _set, _where, _returning, [], false);
    }

    public Task<IReadOnlyList<T>> ToListAsync<T>(
        Func<System.Data.Common.DbDataReader, T> map,
        CancellationToken cancellationToken = default)
        => Executor().QueryAsync(Build(), map, Overlay, cancellationToken);

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var query = Build();
        var affected = await Executor().ExecuteAsync(query, Overlay, cancellationToken);
        if (_expect is int expected && affected != expected)
        {
            throw new ConcurrencyException(expected, affected);
        }

        return affected;
    }

    private IQueryExecutor Executor()
        => _executor ?? throw new InvalidOperationException("This query is not bound to a database.");

    private void EnsureVersionInWhere()
    {
        foreach (var column in _table.Columns)
        {
            if (column.IsVersion && !ContainsColumn(_where, column))
            {
                throw new InvalidOperationException("Version column must appear in WHERE");
            }
        }
    }

    private static bool ContainsColumn(Expr? expr, IColumn column)
    {
        if (expr is null)
        {
            return false;
        }

        var expected = column.ToRef();
        return expr switch
        {
            ColumnRef actual =>
                actual.TableAlias == expected.TableAlias && actual.ColumnName == expected.ColumnName,
            BinaryExpr binary => ContainsColumn(binary.Left, column) || ContainsColumn(binary.Right, column),
            UnaryExpr unary => ContainsColumn(unary.Operand, column),
            InExpr inn => ContainsColumn(inn.Needle, column) || inn.Haystack.Any(item => ContainsColumn(item, column)),
            BetweenExpr between =>
                ContainsColumn(between.Value, column)
                || ContainsColumn(between.Lo, column)
                || ContainsColumn(between.Hi, column),
            CoalesceExpr coalesce => coalesce.Args.Any(arg => ContainsColumn(arg, column)),
            AggregateExpr { Arg: not null } agg => ContainsColumn(agg.Arg, column),
            CallExpr call => call.Args.Any(arg => ContainsColumn(arg, column)),
            _ => false
        };
    }

    private UpdateBuilder Copy(
        EquatableList<(string Column, Expr Value)>? set = null,
        Expr? where = null,
        EquatableList<SelectItem>? returning = null,
        int? expect = null,
        QueryOptions? overlay = null)
        => new(
            _table,
            _executor,
            overlay ?? Overlay,
            set ?? _set,
            where ?? _where,
            returning ?? _returning,
            expect ?? _expect);
}
