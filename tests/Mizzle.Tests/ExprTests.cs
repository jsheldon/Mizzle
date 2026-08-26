namespace Mizzle.Tests;

public sealed class ExprTests
{
    [Fact]
    public void ParamBag_assigns_monotonic_slots()
    {
        var bag = new ParamBag();
        var a = bag.Add("x", typeof(string));
        var b = bag.Add(1, typeof(int));
        Assert.Equal(0, a.Slot);
        Assert.Equal(1, b.Slot);
        Assert.Equal(["x", 1], bag.Values);
    }

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
