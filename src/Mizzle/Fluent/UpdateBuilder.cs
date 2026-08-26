using Mizzle.Ir;
using Mizzle.Schema;

namespace Mizzle.Fluent;

public sealed class UpdateBuilder
{
    private readonly ITable _table;
    private readonly IQueryExecutor? _executor;
    private readonly IReadOnlyList<(string Column, Expr Value)> _set;
    private readonly Expr? _where;
    private readonly int? _expect;

    public UpdateBuilder(ITable table, ParamBag parameters, IQueryExecutor? executor = null, QueryOptions? overlay = null)
        : this(table, parameters, executor, overlay, [], null, null)
    {
    }

    private UpdateBuilder(
        ITable table,
        ParamBag parameters,
        IQueryExecutor? executor,
        QueryOptions? overlay,
        IReadOnlyList<(string Column, Expr Value)> set,
        Expr? where,
        int? expect)
    {
        _table = table;
        Parameters = parameters;
        _executor = executor;
        Overlay = overlay;
        _set = set;
        _where = where;
        _expect = expect;
    }

    public ParamBag Parameters { get; }

    public QueryOptions? Overlay { get; }

    public UpdateBuilder Set(IColumn column, object? value)
    {
        var param = Parameters.Add(value, column.ClrType);
        return Copy(set: [.._set, (column.Name, (Expr)param)]);
    }

    public UpdateBuilder Where(Expr expr)
        => Copy(where: _where is null ? expr : Sql.And(_where, expr));

    public UpdateBuilder Where(IColumn column, object? value)
    {
        var param = Parameters.Add(value, column.ClrType);
        return Where(new BinaryExpr(BinaryOp.Eq, column.ToRef(), param));
    }

    public UpdateBuilder Expect(int affectedRows) => Copy(expect: affectedRows);

    public UpdateBuilder Timeout(TimeSpan timeout) => Copy(overlay: new QueryOptions(timeout));

    public UpdateQuery Build()
    {
        EnsureVersionInWhere();
        if (_set.Count == 0)
        {
            throw new InvalidOperationException("SET is required.");
        }

        return new UpdateQuery(_table.ToFrom(), _set, _where, [], [], false);
    }

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var query = Build();
        var affected = await Executor().ExecuteAsync(query, Parameters, Overlay, cancellationToken);
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
        IReadOnlyList<(string Column, Expr Value)>? set = null,
        Expr? where = null,
        int? expect = null,
        QueryOptions? overlay = null)
        => new(
            _table,
            Parameters,
            _executor,
            overlay ?? Overlay,
            set ?? _set,
            where ?? _where,
            expect ?? _expect);
}
