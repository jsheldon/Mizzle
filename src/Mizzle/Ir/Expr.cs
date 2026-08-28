namespace Mizzle.Ir;

public abstract record Expr;

public sealed record ColumnRef(string TableAlias, string ColumnName, Type ClrType) : Expr
{
    /// <summary>Lets a column reference sit in a select list unaliased.</summary>
    public static implicit operator SelectItem(ColumnRef column) => new(column, null);
}

public sealed record ParamRef(int Slot, Type ClrType) : Expr;

// A value captured at query-build time. Never reaches an emitter: the
// parameterization pass replaces it with a ParamRef and extracts the value.
public sealed record ValueExpr(object? Value, Type ClrType) : Expr;

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

public sealed record InExpr(Expr Needle, EquatableList<Expr> Haystack) : Expr;

public sealed record BetweenExpr(Expr Value, Expr Lo, Expr Hi) : Expr;

public sealed record CoalesceExpr(EquatableList<Expr> Args) : Expr;

public enum AggregateKind
{
    Count,
    Sum,
    Avg,
    Min,
    Max
}

public sealed record AggregateExpr(AggregateKind Kind, Expr? Arg) : Expr;

public sealed record CallExpr(string Name, EquatableList<Expr> Args, DialectKind Dialect) : Expr;

public static class QueryShape
{
    public static Expr StripValues(Expr expr) => expr;
}
