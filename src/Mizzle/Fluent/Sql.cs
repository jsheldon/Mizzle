using Mizzle.Ir;

namespace Mizzle.Fluent;

public static class Sql
{
    public static BinaryExpr Eq(Expr left, Expr right) => new(BinaryOp.Eq, left, right);

    public static BinaryExpr Eq(ColumnRef column, object? value, ParamBag bag)
        => new(BinaryOp.Eq, column, bag.Add(value, column.ClrType));

    public static BinaryExpr And(Expr left, Expr right) => new(BinaryOp.And, left, right);

    public static BinaryExpr Or(Expr left, Expr right) => new(BinaryOp.Or, left, right);

    public static UnaryExpr Not(Expr operand) => new(UnaryOp.Not, operand);

    public static UnaryExpr IsNull(Expr operand) => new(UnaryOp.IsNull, operand);

    public static UnaryExpr IsNotNull(Expr operand) => new(UnaryOp.IsNotNull, operand);

    public static BinaryExpr Like(Expr left, Expr right) => new(BinaryOp.Like, left, right);

    public static InExpr In(Expr needle, IReadOnlyList<Expr> haystack) => new(needle, [..haystack]);

    public static BetweenExpr Between(Expr value, Expr lo, Expr hi) => new(value, lo, hi);

    public static CoalesceExpr Coalesce(params Expr[] args) => new([..args]);

    public static AggregateExpr Count() => new(AggregateKind.Count, null);

    public static AggregateExpr Count(Expr arg) => new(AggregateKind.Count, arg);
}
