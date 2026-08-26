namespace Mizzle.Ir;

public abstract record Expr;

public sealed record ColumnRef(string TableAlias, string ColumnName, Type ClrType) : Expr;

public sealed record ParamRef(int Slot, Type ClrType) : Expr;

public sealed record BinaryExpr(BinaryOp Op, Expr Left, Expr Right) : Expr;

public enum BinaryOp
{
    Eq,
    Ne,
    Gt,
    Gte,
    Lt,
    Lte,
    And,
    Or,
    Like,
    ILike
}

public sealed record UnaryExpr(UnaryOp Op, Expr Operand) : Expr;

public enum UnaryOp
{
    Not,
    IsNull,
    IsNotNull
}

public sealed record InExpr(Expr Needle, IReadOnlyList<Expr> Haystack) : Expr;

public sealed record BetweenExpr(Expr Value, Expr Lo, Expr Hi) : Expr;

public sealed record CoalesceExpr(IReadOnlyList<Expr> Args) : Expr;

public enum AggregateKind
{
    Count,
    Sum,
    Avg,
    Min,
    Max
}

public sealed record AggregateExpr(AggregateKind Kind, Expr? Arg) : Expr;

public sealed record CallExpr(string Name, IReadOnlyList<Expr> Args, DialectKind Dialect) : Expr;

public static class QueryShape
{
    public static Expr StripValues(Expr expr) => expr;
}
