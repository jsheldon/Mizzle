namespace Mizzle.Tests;

file sealed class Authors : PgTable<Authors>
{
    public Authors() : base("authors") { }
    public PgColumn<Guid> AuthorId { get; } = Uuid("author_id");
    public PgColumn<string> DisplayName { get; } = Text("display_name");
    public PgColumn<Guid> BlogId { get; } = Uuid("blog_id");
}

file sealed class Posts : PgTable<Posts>
{
    public Posts() : base("posts") { }
    public PgColumn<Guid> AuthorId { get; } = Uuid("author_id");
}

public sealed class SelectBuilderWhereTests
{
    private static (string Sql, IReadOnlyList<object?> Values) EmitPg(SelectQuery q)
    {
        var (canonical, values) = Parameterizer.Run(q);
        return (new PgEmitter().Emit(canonical, values).Sql, values);
    }

    [Fact]
    public void WithAlias_lets_one_query_join_a_table_twice()
    {
        var authors = new Authors().WithAlias("a");
        var other = new Authors().WithAlias("a2");

        var builder = new SelectBuilder()
            .Select(authors.DisplayName, other.DisplayName.As("OtherName"))
            .From(authors.ToFrom())
            .InnerJoin(other, Sql.Eq(authors.BlogId, other.BlogId));

        var (sql, _) = EmitPg(builder.Build());
        Assert.Equal(
            "SELECT \"a\".\"display_name\", \"a2\".\"display_name\" AS \"OtherName\" "
            + "FROM \"authors\" AS \"a\" INNER JOIN \"authors\" AS \"a2\" ON \"a\".\"blog_id\" = \"a2\".\"blog_id\"",
            sql);
        Assert.Equal("a", authors.Alias);
        Assert.Equal("authors", new Authors().Alias);
    }

    [Fact]
    public void Select_alias_emits_as_clause_and_keeps_table_alias()
    {
        var authors = new Authors();

        var builder = new SelectBuilder()
            .Select(authors.AuthorId.As("Id"), authors.DisplayName)
            .From(authors.ToFrom());

        var (sql, _) = EmitPg(builder.Build());
        Assert.Equal(
            "SELECT \"authors\".\"author_id\" AS \"Id\", \"authors\".\"display_name\" FROM \"authors\" AS \"authors\"",
            sql);
    }

    [Fact]
    public void Table_join_overloads_and_column_eq_read_cleanly()
    {
        var authors = new Authors();
        var posts = new Posts();
        var blogId = Guid.NewGuid();

        var builder = new SelectBuilder()
            .Select(authors.DisplayName)
            .From(authors.ToFrom())
            .InnerJoin(posts, Sql.Eq(authors.AuthorId, posts.AuthorId))
            .Where(authors.BlogId, blogId);

        var (sql, values) = EmitPg(builder.Build());
        Assert.Equal(
            "SELECT \"authors\".\"display_name\" FROM \"authors\" AS \"authors\" INNER JOIN \"posts\" AS \"posts\" ON \"authors\".\"author_id\" = \"posts\".\"author_id\" WHERE \"authors\".\"blog_id\" = $1",
            sql);
        Assert.Equal([blogId], values);
    }

    [Fact]
    public void Chained_where_calls_and_combine()
    {
        var email = new ColumnRef("u", "email", typeof(string));
        var id = new ColumnRef("u", "id", typeof(int));
        var built = new SelectBuilder()
            .Select(email)
            .From(new FromSource("users", "public", "u"))
            .Where(Sql.Eq(email, "a@b.com"))
            .Where(Sql.Eq(id, 7))
            .Build();

        var (sql, values) = EmitPg(built);
        Assert.Equal(
            "SELECT \"u\".\"email\" FROM \"public\".\"users\" AS \"u\" WHERE (\"u\".\"email\" = $1 AND \"u\".\"id\" = $2)",
            sql);
        Assert.Equal(["a@b.com", 7], values);
    }
}

public sealed class SelectBuilderTests
{
    [Fact]
    public void Fluent_select_where_matches_hand_built_ir()
    {
        var email = new ColumnRef("u", "email", typeof(string));
        var built = new SelectBuilder()
            .Select(email)
            .From(new FromSource("users", "public", "u"))
            .Where(Sql.Eq(email, "a@b.com"))
            .Limit(10)
            .Build();

        Assert.NotNull(built.Where);
        Assert.Equal(10, built.Limit);
        var (canonical, values) = Parameterizer.Run(built);
        Assert.Equal(["a@b.com"], values);
        Assert.Equal(
            "SELECT \"u\".\"email\" FROM \"public\".\"users\" AS \"u\" WHERE \"u\".\"email\" = $1 LIMIT 10",
            new PgEmitter().Emit(canonical, values).Sql);
    }
}
