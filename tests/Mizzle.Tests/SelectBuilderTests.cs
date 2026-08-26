namespace Mizzle.Tests;

public sealed class SelectBuilderTests
{
    [Fact]
    public void Fluent_select_where_matches_hand_built_ir()
    {
        var bag = new ParamBag();
        var email = new ColumnRef("u", "email", typeof(string));
        var built = new SelectBuilder(bag)
            .Select(email)
            .From(new FromSource("users", "public", "u"))
            .Where(Sql.Eq(email, "a@b.com", bag))
            .Limit(10)
            .Build();

        Assert.NotNull(built.Where);
        Assert.Equal(10, built.Limit);
        Assert.Equal(["a@b.com"], bag.Values);
        Assert.Equal(
            "SELECT \"u\".\"email\" FROM \"public\".\"users\" AS \"u\" WHERE \"u\".\"email\" = $1 LIMIT 10",
            new PgEmitter().Emit(built, bag).Sql);
    }
}
