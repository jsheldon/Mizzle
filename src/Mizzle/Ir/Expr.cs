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
    ILike,
    Add,
    Subtract
}

// A LIKE/ILIKE with an explicit ESCAPE character, kept separate from BinaryExpr's
// plain Like/ILike ops (which stay pattern-only, no escape clause) rather than
// growing BinaryExpr an optional field every other BinaryOp would carry for nothing.
public sealed record LikeExpr(Expr Left, Expr Right, char Escape, bool CaseInsensitive) : Expr;

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

// ArgClrType is the argument's own CLR type (set for Sum/Avg only, by the
// Column<T>-typed Sql.Sum/Sql.Avg overloads) -- SUM and AVG don't return that
// same type on both dialects (e.g. Postgres's SUM(int) is bigint; SQL Server's
// stays int), so the emitters use it to cast toward a result type that is
// correct, and identical, on both. Null for Count/Min/Max (no promotion ever
// applies) and for the Expr-typed escape-hatch overloads (no known argument
// type to promote from -- the caller's stated result type is trusted as-is).
public sealed record AggregateExpr(AggregateKind Kind, Expr? Arg, Type? ArgClrType = null) : Expr;

public sealed record CallExpr(string Name, EquatableList<Expr> Args, DialectKind Dialect) : Expr;

// The next value from a database sequence: NEXT VALUE FOR <seq> on SQL Server,
// nextval('<seq>') on Postgres. Portable across both dialects, so it isn't a CallExpr
// (SQL Server's syntax isn't a function call) and needs no Feature/CapabilityChecker gate.
public sealed record NextValueExpr(string Sequence) : Expr;

// T-SQL CONVERT(type, expr [, style]). SqlType is a type name and Style is a
// T-SQL style code, not values, so neither is parameterized.
public sealed record ConvertExpr(string SqlType, Expr Value, int? Style = null) : Expr;

/// <summary>One <c>WHEN condition THEN result</c> arm of a <see cref="CaseExpr"/>.</summary>
public sealed record CaseWhen(Expr Condition, Expr Result);

// Searched CASE: CASE WHEN c THEN r ... [ELSE e] END. Standard on both dialects.
// With no ELSE the result is NULL where no arm matches, so a projected CaseExpr
// is nullable unless the target says otherwise.
public sealed record CaseExpr(EquatableList<CaseWhen> Whens, Expr? Fallback = null) : Expr
{
    /// <summary>Returns this CASE with an <c>ELSE</c> arm.</summary>
    public CaseExpr Else(Expr result) => this with { Fallback = result };

    /// <summary>Returns this CASE with a literal <c>ELSE</c> arm.</summary>
    // Constrained to a value type for the same reason Sql.When<T> is: an exact
    // generic match must not out-rank Else(Expr) on an Expr-derived result.
    public CaseExpr Else<T>(T result) where T : struct => Else(new ValueExpr(result, typeof(T)));

    /// <summary>Returns this CASE with a literal string <c>ELSE</c> arm.</summary>
    public CaseExpr Else(string result) => Else(new ValueExpr(result, typeof(string)));
}

// A ranking window function: ROW_NUMBER() OVER (PARTITION BY ... ORDER BY
// ...). Reuses OrderByItem so a multi-column, mixed-direction tie-break
// costs nothing new. PartitionColumns may be empty (no PARTITION BY clause);
// OrderColumns may not (an emitter rejects a ROW_NUMBER with no ORDER BY).
public sealed record RowNumberExpr(
    EquatableList<Expr> PartitionColumns,
    EquatableList<OrderByItem> OrderColumns) : Expr
{
    public RowNumberExpr PartitionBy(params Expr[] columns) => this with { PartitionColumns = [.. columns] };

    public RowNumberExpr OrderBy(Expr expr)
        => this with
        {
            OrderColumns = [.. OrderColumns, new OrderByItem(expr, false)]
        };

    public RowNumberExpr OrderByDesc(Expr expr)
        => this with
        {
            OrderColumns = [.. OrderColumns, new OrderByItem(expr, true)]
        };
}

public static class QueryShape
{
    public static Expr StripValues(Expr expr) => expr;
}
