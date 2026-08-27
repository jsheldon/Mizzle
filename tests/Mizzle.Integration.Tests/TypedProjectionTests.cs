namespace Mizzle.Integration.Tests;

file sealed class ProjPersons : PgTable<ProjPersons>
{
    public ProjPersons() : base("proj_person", "public", "a") { }

    public PgColumn<Guid> PersonId { get; } = Uuid("person_id").PrimaryKey();
    public PgColumn<Guid> LanguageId { get; } = Uuid("language_id");
    public PgColumn<string> FirstName { get; } = Text("first_name").NotNull();
}

file sealed class ProjLists : PgTable<ProjLists>
{
    public ProjLists() : base("proj_lists", "public", "c") { }

    public PgColumn<Guid> ItemId { get; } = Uuid("item_id").PrimaryKey();
    public PgColumn<string> ItemDesc { get; } = Text("item_desc").NotNull();
    public PgColumn<string> ListType { get; } = Text("list_type").NotNull();
}

public sealed class TypedProjectionTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fx;

    public TypedProjectionTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Generate_mode_projects_joined_rows_with_left_join_null()
    {
        var withLanguage = Guid.NewGuid();
        var withoutLanguage = Guid.NewGuid();
        var languageId = Guid.NewGuid();
        await using (var conn = await _fx.DataSource.OpenConnectionAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS public.proj_person (
                  person_id uuid PRIMARY KEY,
                  language_id uuid NULL,
                  first_name text NOT NULL
                );
                CREATE TABLE IF NOT EXISTS public.proj_lists (
                  item_id uuid PRIMARY KEY,
                  item_desc text NOT NULL,
                  list_type text NOT NULL
                );
                DELETE FROM public.proj_person;
                DELETE FROM public.proj_lists;
                INSERT INTO public.proj_lists (item_id, item_desc, list_type)
                  VALUES ('{languageId}', 'English', 'language');
                INSERT INTO public.proj_person (person_id, language_id, first_name)
                  VALUES ('{withLanguage}', '{languageId}', 'Ada');
                INSERT INTO public.proj_person (person_id, language_id, first_name)
                  VALUES ('{withoutLanguage}', NULL, 'Grace');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new PostgresDb(_fx.DataSource);
        var a = new ProjPersons();
        var c = new ProjLists();

        // Generate mode: ProfileProjection does not exist anywhere — the
        // generator declares it from this select shape.
        var rows = await db.Select(a.PersonId, a.FirstName, c.ItemDesc)
            .From(a)
            .LeftJoin(c).On(a.LanguageId.Eq(c.ItemId), c.ListType.Eq("language"))
            .OrderBy(a.FirstName)
            .ToListAsync<ProfileProjection>();

        Assert.Equal(2, rows.Count);
        Assert.Equal("Ada", rows[0].FirstName);
        Assert.Equal("English", rows[0].ItemDesc);
        Assert.Equal(withLanguage, rows[0].PersonId);
        Assert.Equal("Grace", rows[1].FirstName);
        Assert.Null(rows[1].ItemDesc);
    }

    [Fact]
    public async Task FirstOrDefault_projection_returns_null_when_missing()
    {
        await using (var conn = await _fx.DataSource.OpenConnectionAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS public.proj_person (
                  person_id uuid PRIMARY KEY,
                  language_id uuid NULL,
                  first_name text NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new PostgresDb(_fx.DataSource);
        var a = new ProjPersons();
        var missing = await db.Select(a.PersonId, a.FirstName)
            .From(a)
            .Where(a.PersonId.Eq(Guid.NewGuid()))
            .FirstOrDefaultAsync<MiniProjection>();
        Assert.Null(missing);
    }
}
