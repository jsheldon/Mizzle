namespace Mizzle.Tests;

public sealed class FeatureCollectorTests
{
    private static SelectQuery Base() => new(
        Select: [new SelectItem(new ColumnRef("u", "email", typeof(string)), null)],
        From: new FromSource("users", "dbo", "u"),
        Joins: [], Where: null, OrderBy: [], Limit: null, Offset: null,
        Distinct: false, With: [], RecursiveWith: false, UnionAll: []);

    private static BinaryExpr ILike(ParamBag bag)
        => new(BinaryOp.ILike, new ColumnRef("u", "email", typeof(string)), bag.Add("%x%", typeof(string)));

    [Fact]
    public void Ilike_in_having_is_collected_and_throws_on_sql_server()
    {
        var bag = new ParamBag();
        var q = Base() with
        {
            GroupBy = new EquatableList<Expr>([new ColumnRef("u", "email", typeof(string))]),
            Having = ILike(bag)
        };
        var ex = Assert.Throws<UnsupportedFeatureException>(() => new SqlServerEmitter().Emit(q, bag));
        Assert.Equal(Feature.ILike, ex.Feature);
    }

    [Fact]
    public void Ilike_in_order_by_is_collected()
    {
        var bag = new ParamBag();
        var q = Base() with { OrderBy = [new OrderByItem(ILike(bag), false)] };
        Assert.Contains(Feature.ILike, FeatureCollector.Collect(q));
    }

    [Fact]
    public void Ilike_in_select_list_is_collected()
    {
        var bag = new ParamBag();
        var q = Base() with { Select = [new SelectItem(ILike(bag), "flag")] };
        Assert.Contains(Feature.ILike, FeatureCollector.Collect(q));
    }

    [Fact]
    public void Ilike_in_update_set_is_collected()
    {
        var bag = new ParamBag();
        var q = new UpdateQuery(
            Table: new FromSource("users", "dbo", "u"),
            Set: [("flag", (Expr)ILike(bag))],
            Where: null, Returning: [], With: [], RecursiveWith: false);
        Assert.Contains(Feature.ILike, FeatureCollector.Collect(q));
    }
}
