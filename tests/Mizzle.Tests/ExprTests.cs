namespace Mizzle.Tests;

public sealed class ExprTests
{
    [Fact]
    public void Two_eq_trees_with_different_values_are_equal_as_expr()
    {
        var left = new BinaryExpr(
            BinaryOp.Eq,
            new ColumnRef("u", "email", typeof(string)),
            new ParamRef(0, typeof(string)));
        var right = new BinaryExpr(
            BinaryOp.Eq,
            new ColumnRef("u", "email", typeof(string)),
            new ParamRef(0, typeof(string)));
        Assert.Equal(left, right);
    }
}
