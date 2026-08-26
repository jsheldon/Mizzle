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
    private readonly int? _expect;

    public DeleteBuilder(ITable table, ParamBag parameters, IQueryExecutor? executor = null, QueryOptions? overlay = null)
        : this(table, parameters, executor, overlay, null, [], null)
    {
    }

    private DeleteBuilder(
        ITable table,
        ParamBag parameters,
        IQueryExecutor? executor,
        QueryOptions? overlay,
        Expr? where,
        EquatableList<SelectItem> returning,
        int? expect)
    {
        _table = table;
        Parameters = parameters;
        _executor = executor;
        Overlay = overlay;
        _where = where;
        _returning = returning;
        _expect = expect;
    }

    public ParamBag Parameters { get; }

    public QueryOptions? Overlay { get; }

    public DeleteBuilder Where(Expr expr)
        => Copy(where: _where is null ? expr : Sql.And(_where, expr));

    public DeleteBuilder Where(IColumn column, object? value)
    {
        var param = Parameters.Add(value, column.ClrType);
        return Where(new BinaryExpr(BinaryOp.Eq, column.ToRef(), param));
    }

    public DeleteBuilder Returning(params IColumn[] columns)
        => Copy(returning: [..columns.Select(c => new SelectItem(c.ToRef(), null))]);

    public DeleteBuilder Expect(int affectedRows) => Copy(expect: affectedRows);

    public DeleteBuilder Timeout(TimeSpan timeout) => Copy(overlay: new QueryOptions(timeout));

    public DeleteQuery Build() => new(_table.ToFrom(), _where, _returning, [], false);

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var affected = await Executor().ExecuteAsync(Build(), Parameters, Overlay, cancellationToken);
        if (_expect is int expected && affected != expected)
        {
            throw new ConcurrencyException(expected, affected);
        }

        return affected;
    }

    public Task<IReadOnlyList<T>> ToListAsync<T>(
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
        => Executor().QueryAsync(Build(), Parameters, map, Overlay, cancellationToken);

    private IQueryExecutor Executor()
        => _executor ?? throw new InvalidOperationException("This query is not bound to a database.");

    private DeleteBuilder Copy(
        Expr? where = null,
        EquatableList<SelectItem>? returning = null,
        int? expect = null,
        QueryOptions? overlay = null)
        => new(
            _table,
            Parameters,
            _executor,
            overlay ?? Overlay,
            where ?? _where,
            returning ?? _returning,
            expect ?? _expect);
}
