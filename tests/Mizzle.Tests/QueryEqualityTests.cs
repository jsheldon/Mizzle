namespace Mizzle.Tests;

public sealed class QueryEqualityTests
{
    private static SelectQuery Build()
    {
        var bag = new ParamBag();
        var p = bag.Add("a@b.com", typeof(string));
        return new SelectQuery(
            Select: [new SelectItem(new ColumnRef("u", "email", typeof(string)), null)],
            From: new FromSource("users", "public", "u"),
            Joins: [],
            Where: new BinaryExpr(BinaryOp.Eq, new ColumnRef("u", "email", typeof(string)), p),
            OrderBy: [],
            Limit: 10,
            Offset: null,
            Distinct: false,
            With: [],
            RecursiveWith: false,
            UnionAll: []);
    }

    [Fact]
    public void Structurally_identical_selects_are_equal_with_equal_hashes()
    {
        var a = Build();
        var b = Build();
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Different_limit_is_not_equal()
    {
        var a = Build();
        var b = Build() with { Limit = 11 };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Structurally_identical_inserts_are_equal()
    {
        InsertQuery Make()
        {
            var bag = new ParamBag();
            var v = bag.Add("a@b.com", typeof(string));
            return new InsertQuery(
                Into: new FromSource("users", "public", "u"),
                Columns: ["email"],
                ValuesRows: [[v]],
                FromSelect: null,
                Returning: [new SelectItem(new ColumnRef("u", "id", typeof(int)), null)],
                With: [],
                RecursiveWith: false);
        }

        Assert.Equal(Make(), Make());
    }
}
