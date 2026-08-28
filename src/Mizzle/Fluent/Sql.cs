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

    /// <summary>A <c>BETWEEN</c> range test, inclusive of both bounds.</summary>
    public static BetweenExpr Between(Expr value, Expr lo, Expr hi) => new(value, lo, hi);

    /// <summary>Returns the first non-null argument, as SQL <c>COALESCE</c>.</summary>
    public static CoalesceExpr Coalesce(params Expr[] args) => new([..args]);

    /// <summary>A <c>COUNT</c> aggregate.</summary>
    public static AggregateExpr Count() => new(AggregateKind.Count, null);

    public static AggregateExpr Count(Expr arg) => new(AggregateKind.Count, arg);
}
