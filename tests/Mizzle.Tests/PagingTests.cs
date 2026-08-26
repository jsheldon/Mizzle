namespace Mizzle.Tests;

file sealed class Users : PgTable<Users>
{
    public Users() : base("users", "public", "u") { }

    public PgColumn<int> Id { get; } = Identity("id");
    public PgColumn<string> Email { get; } = Text("email");
}

public sealed class PagingTests
{
    [Fact]
    public void Page_sets_limit_and_offset()
    {
        var q = new SelectBuilder(new ParamBag())
            .Select(new ColumnRef("u", "email", typeof(string)))
            .From(new FromSource("users", "public", "u"))
            .OrderBy(new ColumnRef("u", "email", typeof(string)))
            .Page(2, 10)
            .Build();
        Assert.Equal(10, q.Limit);
        Assert.Equal(10, q.Offset);
    }

    [Fact]
    public void Page_rejects_page_less_than_one()
    {
        var builder = new SelectBuilder(new ParamBag())
            .Select(new ColumnRef("u", "email", typeof(string)))
            .From(new FromSource("users", "public", "u"));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Page(0, 10));
    }

    [Fact]
    public void WindowCount_appends_count_over()
    {
        var query = new SelectQuery(
            Select: [new SelectItem(new ColumnRef("u", "email", typeof(string)), null)],
            From: new FromSource("users", "public", "u"),
            Joins: [],
            Where: null,
            OrderBy: [],
            Limit: null,
            Offset: null,
            Distinct: false,
            With: [],
            RecursiveWith: false,
            UnionAll: [],
            WindowCount: true);
        var sql = new PgEmitter().Emit(query, new ParamBag());
        Assert.Contains("count(*) OVER()", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("mizzle_total", sql.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void After_requires_order_by()
    {
        var users = new Users();
        var builder = new SelectBuilder(new ParamBag())
            .Select(users.Email)
            .From(users.ToFrom());
        var ex = Assert.Throws<InvalidOperationException>(() => builder.After((users.Email, "a@b.com")));
        Assert.Equal("ORDER BY is required for After.", ex.Message);
    }

    [Fact]
    public void After_adds_seek_predicate()
    {
        var users = new Users();
        var bag = new ParamBag();
        var q = new SelectBuilder(bag)
            .Select(users.Email)
            .From(users.ToFrom())
            .OrderBy(users.Email.ToRef())
            .After((users.Email, "a@b.com"))
            .Build();
        Assert.NotNull(q.Where);
        Assert.Equal(
            "SELECT \"u\".\"email\" FROM \"public\".\"users\" AS \"u\" WHERE \"u\".\"email\" > $1 ORDER BY \"u\".\"email\"",
            new PgEmitter().Emit(q, bag).Sql);
    }
}
