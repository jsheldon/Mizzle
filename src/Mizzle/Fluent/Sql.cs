using Mizzle.Ir;
using Mizzle.Schema;

namespace Mizzle.Fluent;

/// <summary>
///     Free-standing predicate and expression helpers, for the shapes the column
///     operators do not cover.
/// </summary>
public static class Sql
{
    /// <summary>An equality comparison.</summary>
    public static BinaryExpr Eq(Expr left, Expr right) => new(BinaryOp.Eq, left, right);

    public static BinaryExpr Eq(IColumn left, IColumn right) => new(BinaryOp.Eq, left.ToRef(), right.ToRef());

    public static BinaryExpr Eq(ColumnRef column, object? value)
        => new(BinaryOp.Eq, column, new ValueExpr(value, column.ClrType));

    /// <summary>Combines conditions with <c>AND</c>.</summary>
    public static BinaryExpr And(Expr left, Expr right) => new(BinaryOp.And, left, right);

    public static BinaryExpr And(params Expr[] conditions) => Fold(BinaryOp.And, conditions);

    /// <summary>Combines conditions with <c>OR</c>.</summary>
    public static BinaryExpr Or(Expr left, Expr right) => new(BinaryOp.Or, left, right);

    public static BinaryExpr Or(params Expr[] conditions) => Fold(BinaryOp.Or, conditions);

    private static BinaryExpr Fold(BinaryOp op, Expr[] conditions)
    {
        if (conditions.Length < 2)
        {
            throw new ArgumentException("At least two conditions are required.", nameof(conditions));
        }

        var result = new BinaryExpr(op, conditions[0], conditions[1]);
        for (var i = 2; i < conditions.Length; i++)
        {
            result = new BinaryExpr(op, result, conditions[i]);
        }

        return result;
    }

    /// <summary>Negates a condition.</summary>
    public static UnaryExpr Not(Expr operand) => new(UnaryOp.Not, operand);

    /// <summary>Tests for <c>NULL</c>.</summary>
    public static UnaryExpr IsNull(Expr operand) => new(UnaryOp.IsNull, operand);

    /// <summary>Tests for a value other than <c>NULL</c>.</summary>
    public static UnaryExpr IsNotNull(Expr operand) => new(UnaryOp.IsNotNull, operand);

    /// <summary>A <c>LIKE</c> pattern match.</summary>
    public static BinaryExpr Like(Expr left, Expr right) => new(BinaryOp.Like, left, right);

    /// <summary>An <c>IN</c> list test.</summary>
    public static InExpr In(Expr needle, IReadOnlyList<Expr> haystack) => new(needle, [..haystack]);

    /// <summary>One arm of a <see cref="Case(CaseWhen[])"/>.</summary>
    public static CaseWhen When(Expr condition, Expr result) => new(condition, result);

    /// <summary>One arm of a <see cref="Case(CaseWhen[])"/> with a literal result.</summary>
    // Constrained to a value type so it cannot out-rank When(Expr, Expr) on an
    // Expr-derived result: an exact generic match would beat the derived-to-base
    // conversion and quietly bind the expression object as a parameter.
    public static CaseWhen When<T>(Expr condition, T result) where T : struct
        => new(condition, Value(result));

    /// <summary>One arm of a <see cref="Case(CaseWhen[])"/> with a string result.</summary>
    public static CaseWhen When(Expr condition, string result) => new(condition, Value(result));

    /// <summary>
    ///     A searched <c>CASE</c>. Arms are tested in order; chain
    ///     <see cref="CaseExpr.Else"/> for the fallback, or leave it off to get
    ///     <c>NULL</c> when nothing matches.
    /// </summary>
    /// <example><code>Sql.Case(Sql.When(c.Kind.Eq(504m), 0)).Else(Sql.Value(4))</code></example>
    public static CaseExpr Case(params CaseWhen[] whens)
        => whens.Length > 0
            ? new CaseExpr([..whens])
            : throw new ArgumentException("A CASE needs at least one WHEN arm.", nameof(whens));

    /// <summary>A <c>BETWEEN</c> range test, inclusive of both bounds.</summary>
    public static BetweenExpr Between(Expr value, Expr lo, Expr hi) => new(value, lo, hi);

    /// <summary>Returns the first non-null argument, as SQL <c>COALESCE</c>.</summary>
    public static CoalesceExpr Coalesce(params Expr[] args) => new([..args]);

    /// <summary>A <c>COUNT</c> aggregate.</summary>
    public static AggregateExpr Count() => new(AggregateKind.Count, null);

    /// <summary>A <c>SUM</c> aggregate.</summary>
    public static AggregateExpr Sum(Expr arg) => new(AggregateKind.Sum, arg);

    /// <summary>A <c>SUM</c> aggregate over a column.</summary>
    public static AggregateExpr Sum(IColumn column) => Sum(column.ToRef());

    /// <summary>An <c>AVG</c> aggregate.</summary>
    public static AggregateExpr Avg(Expr arg) => new(AggregateKind.Avg, arg);

    /// <summary>An <c>AVG</c> aggregate over a column.</summary>
    public static AggregateExpr Avg(IColumn column) => Avg(column.ToRef());

    /// <summary>A <c>MIN</c> aggregate.</summary>
    public static AggregateExpr Min(Expr arg) => new(AggregateKind.Min, arg);

    /// <summary>A <c>MIN</c> aggregate over a column.</summary>
    public static AggregateExpr Min(IColumn column) => Min(column.ToRef());

    /// <summary>A <c>MAX</c> aggregate.</summary>
    public static AggregateExpr Max(Expr arg) => new(AggregateKind.Max, arg);

    /// <summary>A <c>MAX</c> aggregate over a column.</summary>
    public static AggregateExpr Max(IColumn column) => Max(column.ToRef());

    /// <summary>Names an expression in a select list.</summary>
    /// <example><code>Sql.As(Sql.Count(), "Orders")</code></example>
    public static SelectItem As(Expr expr, string alias) => new(expr, alias);

    /// <summary>Projects a column held as an <see cref="IColumn"/> rather than a concrete type.</summary>
    public static SelectItem Item(IColumn column) => new(column.ToRef(), column.ProjectionName);

    /// <summary>A literal value, for cases like a constant priority column.</summary>
    public static ValueExpr Value<T>(T value)
        => value is Expr
            // Binding an expression object as a parameter value emits a
            // placeholder where the caller meant the expression's SQL.
            ? throw new ArgumentException(
                "Value expects a literal; pass the expression itself.", nameof(value))
            : new ValueExpr(value, typeof(T));


    public static AggregateExpr Count(Expr arg) => new(AggregateKind.Count, arg);
}
