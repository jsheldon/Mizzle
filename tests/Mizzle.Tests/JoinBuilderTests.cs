namespace Mizzle.Tests;

file sealed class Persons : PgTable<Persons>
{
    public Persons() : base("person", alias: "a") { }
    public PgColumn<Guid> PersonId { get; } = Uuid("person_id").PrimaryKey();
    public PgColumn<Guid> LanguageId { get; } = Uuid("language_id");
    public PgColumn<string> FirstName { get; } = Text("first_name").NotNull();
}

file sealed class MstrLists : PgTable<MstrLists>
{
    public MstrLists() : base("mstr_lists", alias: "c") { }
    public PgColumn<Guid> ItemId { get; } = Uuid("mstr_list_item_id").PrimaryKey();
    public PgColumn<string> ItemDesc { get; } = Text("mstr_list_item_desc");
    public PgColumn<string> ListType { get; } = Text("mstr_list_type").NotNull();
}

public sealed class JoinBuilderTests
{
    [Fact]
    public void Fluent_left_join_with_constant_condition()
    {
        var a = new Persons();
        var c = new MstrLists();
        var built = new SelectBuilder()
            .Select(a.FirstName, c.ItemDesc)
            .From(a)
            .LeftJoin(c).On(a.LanguageId.Eq(c.ItemId), c.ListType.Eq("language"))
            .Where(a.PersonId.Eq(Guid.Empty))
            .OrderBy(a.FirstName)
            .Build();

        var (canonical, values) = Parameterizer.Run(built);
        var sql = new PgEmitter().Emit(canonical, values).Sql;
        Assert.Equal(
            "SELECT \"a\".\"first_name\", \"c\".\"mstr_list_item_desc\" FROM \"person\" AS \"a\" LEFT JOIN \"mstr_lists\" AS \"c\" ON (\"a\".\"language_id\" = \"c\".\"mstr_list_item_id\" AND \"c\".\"mstr_list_type\" = $1) WHERE \"a\".\"person_id\" = $2 ORDER BY \"a\".\"first_name\"",
            sql);
        Assert.Equal(["language", Guid.Empty], values);
    }

    [Fact]
    public void OrderByDesc_column_overload()
    {
        var a = new Persons();
        var built = new SelectBuilder()
            .Select(a.FirstName)
            .From(a)
            .OrderByDesc(a.FirstName)
            .Build();
        var item = Assert.Single(built.OrderBy);
        Assert.True(item.Descending);
        Assert.Equal(new ColumnRef("a", "first_name", typeof(string)), item.Expr);
    }
}
