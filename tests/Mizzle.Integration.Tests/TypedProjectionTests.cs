namespace Mizzle.Integration.Tests;

file sealed class ProjAuthors : PgTable<ProjAuthors>
{
    public ProjAuthors() : base("proj_authors", "public") { }

    public PgColumn<Guid> AuthorId { get; } = Uuid("author_id").PrimaryKey();
    public PgColumn<Guid> FavoriteTagId { get; } = Uuid("favorite_tag_id");
    public PgColumn<string> DisplayName { get; } = Text("display_name").NotNull();
}

file sealed class ProjTags : PgTable<ProjTags>
{
    public ProjTags() : base("proj_tags", "public") { }

    public PgColumn<Guid> TagId { get; } = Uuid("tag_id").PrimaryKey();
    public PgColumn<string> Label { get; } = Text("label").NotNull();
    public PgColumn<string> Kind { get; } = Text("kind").NotNull();
}

// Deliberately bound (declared here, not synthesized) and not file-scoped: the
// generated mapper lives in a separate generated file and constructs this type
// by name, and the dynamic-mapper test below needs T to already exist so Generate
// mode does not resolve the call instead of the reassigned-local dynamic path.
public sealed record AuthorNameRow(Guid AuthorId, string DisplayName);

public sealed class TypedProjectionTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fx;

    public TypedProjectionTests(PostgresFixture fx) => _fx = fx;

    [DockerFact]
    public async Task Generate_mode_projects_joined_rows_with_left_join_null()
    {
        var taggedAuthor = Guid.NewGuid();
        var untaggedAuthor = Guid.NewGuid();
        var tagId = Guid.NewGuid();
        await using (var conn = await _fx.DataSource.OpenConnectionAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS public.proj_authors (
                  author_id uuid PRIMARY KEY,
                  favorite_tag_id uuid NULL,
                  display_name text NOT NULL
                );
                CREATE TABLE IF NOT EXISTS public.proj_tags (
                  tag_id uuid PRIMARY KEY,
                  label text NOT NULL,
                  kind text NOT NULL
                );
                DELETE FROM public.proj_authors;
                DELETE FROM public.proj_tags;
                INSERT INTO public.proj_tags (tag_id, label, kind)
                  VALUES ('{tagId}', 'Databases', 'topic');
                INSERT INTO public.proj_authors (author_id, favorite_tag_id, display_name)
                  VALUES ('{taggedAuthor}', '{tagId}', 'Ada');
                INSERT INTO public.proj_authors (author_id, favorite_tag_id, display_name)
                  VALUES ('{untaggedAuthor}', NULL, 'Grace');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new PostgresDb(_fx.DataSource);
        var a = new ProjAuthors();
        var t = new ProjTags();

        // Generate mode: AuthorTagProjection does not exist anywhere — the
        // generator declares it from this select shape.
        var rows = await db.Select(a.AuthorId, a.DisplayName, t.Label)
            .From(a)
            .LeftJoin(t).On(a.FavoriteTagId.Eq(t.TagId), t.Kind.Eq("topic"))
            .OrderBy(a.DisplayName)
            .ToListAsync<AuthorTagProjection>();

        Assert.Equal(2, rows.Count);
        Assert.Equal("Ada", rows[0].DisplayName);
        Assert.Equal("Databases", rows[0].Label);
        Assert.Equal(taggedAuthor, rows[0].AuthorId);
        Assert.Equal("Grace", rows[1].DisplayName);
        Assert.Null(rows[1].Label);
    }

    [DockerFact]
    public async Task FirstOrDefault_projection_returns_null_when_missing()
    {
        await using (var conn = await _fx.DataSource.OpenConnectionAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS public.proj_authors (
                  author_id uuid PRIMARY KEY,
                  favorite_tag_id uuid NULL,
                  display_name text NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new PostgresDb(_fx.DataSource);
        var a = new ProjAuthors();
        var missing = await db.Select(a.AuthorId, a.DisplayName)
            .From(a)
            .Where(a.AuthorId.Eq(Guid.NewGuid()))
            .FirstOrDefaultAsync<MiniAuthorProjection>();
        Assert.Null(missing);
    }

    // The whole chain cannot bake -- q is reassigned after declaration -- but the
    // reassignment only adds a Where, so the generator still emits a typed mapper
    // and forwards to the delegate-based overload. This proves both halves for
    // real: the runtime-built WHERE actually filters, and the generated mapper
    // reads the right columns into the right members.
    [DockerFact]
    public async Task Reassigned_local_with_only_a_where_added_still_projects_and_filters()
    {
        var matching = Guid.NewGuid();
        var other = Guid.NewGuid();
        await using (var conn = await _fx.DataSource.OpenConnectionAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS public.proj_authors (
                  author_id uuid PRIMARY KEY,
                  favorite_tag_id uuid NULL,
                  display_name text NOT NULL
                );
                DELETE FROM public.proj_authors;
                INSERT INTO public.proj_authors (author_id, favorite_tag_id, display_name)
                  VALUES ('{matching}', NULL, 'Ada');
                INSERT INTO public.proj_authors (author_id, favorite_tag_id, display_name)
                  VALUES ('{other}', NULL, 'Grace');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new PostgresDb(_fx.DataSource);
        var a = new ProjAuthors();
        var addFilter = true;
        var q = db.Select(a.AuthorId, a.DisplayName).From(a);
        if (addFilter)
        {
            q = q.Where(a.DisplayName.Eq("Ada"));
        }

        var rows = await q.ToListAsync<AuthorNameRow>();

        var row = Assert.Single(rows);
        Assert.Equal(matching, row.AuthorId);
        Assert.Equal("Ada", row.DisplayName);
    }
}
