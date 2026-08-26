namespace Mizzle.Tests;

file sealed class Persons : PgTable<Persons>
{
    public Persons() : base("person", alias: "a") { }
    public PgColumn<Guid> PersonId { get; } = Uuid("person_id");
    public PgColumn<string> FirstName { get; } = Text("first_name");
    public PgColumn<Guid> PracticeId { get; } = Uuid("practice_id");
}

file sealed class Charts : PgTable<Charts>
{
    public Charts() : base("chart", alias: "b") { }
    public PgColumn<Guid> PersonId { get; } = Uuid("person_id");
}

public sealed class SelectBuilderWhereTests
{
    [Fact]
    public void Table_join_overloads_and_column_eq_read_cleanly()
    {
        var a = new Persons();
        var b = new Charts();
        var practiceId = Guid.NewGuid();

        var builder = new SelectBuilder(new ParamBag())
            .Select(a.FirstName)
            .From(a.ToFrom())
            .InnerJoin(b, Sql.Eq(a.PersonId, b.PersonId))
            .Where(a.PracticeId, practiceId);

        var sql = new PgEmitter().Emit(builder.Build(), builder.Parameters);
        Assert.Equal(
            "SELECT \"a\".\"first_name\" FROM \"person\" AS \"a\" INNER JOIN \"chart\" AS \"b\" ON \"a\".\"person_id\" = \"b\".\"person_id\" WHERE \"a\".\"practice_id\" = $1",
            sql.Sql);
        Assert.Equal([practiceId], sql.Parameters);
    }

    [Fact]
    public void Chained_where_calls_and_combine()
    {
        var bag = new ParamBag();
        var email = new ColumnRef("u", "email", typeof(string));
        var id = new ColumnRef("u", "id", typeof(int));
        var built = new SelectBuilder(bag)
            .Select(email)
            .From(new FromSource("users", "public", "u"))
            .Where(Sql.Eq(email, "a@b.com", bag))
            .Where(Sql.Eq(id, 7, bag))
            .Build();

        Assert.Equal(
            "SELECT \"u\".\"email\" FROM \"public\".\"users\" AS \"u\" WHERE (\"u\".\"email\" = $1 AND \"u\".\"id\" = $2)",
            new PgEmitter().Emit(built, bag).Sql);
    }
}

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
