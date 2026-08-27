namespace Mizzle.Tests;

file sealed class Authors : PgTable<Authors>
{
    public Authors() : base("authors", alias: "a") { }
    public PgColumn<Guid> AuthorId { get; } = Uuid("author_id").PrimaryKey();
    public PgColumn<Guid> FavoriteTagId { get; } = Uuid("favorite_tag_id");
    public PgColumn<string> DisplayName { get; } = Text("display_name").NotNull();
}

file sealed class Tags : PgTable<Tags>
{
    public Tags() : base("tags", alias: "t") { }
    public PgColumn<Guid> TagId { get; } = Uuid("tag_id").PrimaryKey();
    public PgColumn<string> Label { get; } = Text("label");
    public PgColumn<string> Kind { get; } = Text("kind").NotNull();
}

public sealed class JoinBuilderTests
{
    [Fact]
    public void Fluent_left_join_with_constant_condition()
    {
        var authors = new Authors();
        var tags = new Tags();
        var built = new SelectBuilder()
            .Select(authors.DisplayName, tags.Label)
            .From(authors)
            .LeftJoin(tags).On(authors.FavoriteTagId.Eq(tags.TagId), tags.Kind.Eq("topic"))
            .Where(authors.AuthorId.Eq(Guid.Empty))
            .OrderBy(authors.DisplayName)
            .Build();

        var (canonical, values) = Parameterizer.Run(built);
        var sql = new PgEmitter().Emit(canonical, values).Sql;
        Assert.Equal(
            "SELECT \"a\".\"display_name\", \"t\".\"label\" FROM \"authors\" AS \"a\" LEFT JOIN \"tags\" AS \"t\" ON (\"a\".\"favorite_tag_id\" = \"t\".\"tag_id\" AND \"t\".\"kind\" = $1) WHERE \"a\".\"author_id\" = $2 ORDER BY \"a\".\"display_name\"",
            sql);
        Assert.Equal(["topic", Guid.Empty], values);
    }

    [Fact]
    public void OrderByDesc_column_overload()
    {
        var a = new Authors();
        var built = new SelectBuilder()
            .Select(a.DisplayName)
            .From(a)
            .OrderByDesc(a.DisplayName)
            .Build();
        var item = Assert.Single(built.OrderBy);
        Assert.True(item.Descending);
        Assert.Equal(new ColumnRef("a", "display_name", typeof(string)), item.Expr);
    }
}
