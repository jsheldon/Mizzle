using Mizzle.Ir;

namespace Mizzle.Fluent;

/// <summary>
///     A typed aggregate result (<c>COUNT</c>/<c>SUM</c>/<c>AVG</c>/<c>MIN</c>/<c>MAX</c>),
///     returned by <see cref="Sql" />'s aggregate factories. Not itself an <see cref="Expr" />
///     -- like <see cref="Mizzle.Schema.Column{T}" />, it converts implicitly and carries typed
///     comparison methods, so <c>Sql.Count().Gt(2L)</c> gets the same compile-time type
///     checking a column comparison does, instead of comparing against a bare object.
/// </summary>
public sealed class Aggregate<T>
{
    private readonly AggregateExpr _expr;

    internal Aggregate(AggregateExpr expr) => _expr = expr;

    /// <summary>Lets an aggregate appear in a select list with an alias.</summary>
    public static implicit operator SelectItem(Aggregate<T> aggregate) => new(aggregate._expr, null);

    /// <summary>Lets an aggregate appear anywhere an expression is expected -- e.g. in a CASE arm.</summary>
    public static implicit operator Expr(Aggregate<T> aggregate) => aggregate._expr;

    /// <summary>Aliases this aggregate's projected column.</summary>
    /// <example><code>Sql.Sum(orders.Total).As("Revenue")</code></example>
    public SelectItem As(string alias) => new(_expr, alias);

    public BinaryExpr Eq(T value) => Binary(BinaryOp.Eq, value);

    public BinaryExpr Ne(T value) => Binary(BinaryOp.Ne, value);

    public BinaryExpr Gt(T value) => Binary(BinaryOp.Gt, value);

    public BinaryExpr Gte(T value) => Binary(BinaryOp.Gte, value);

    public BinaryExpr Lt(T value) => Binary(BinaryOp.Lt, value);

    public BinaryExpr Lte(T value) => Binary(BinaryOp.Lte, value);

    private BinaryExpr Binary(BinaryOp op, T value) => new(op, _expr, new ValueExpr(value, typeof(T)));
}
