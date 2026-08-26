namespace Mizzle.Integration.Tests;

public sealed class PostgresPagingTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fx;

    public PostgresPagingTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task ToPageAsync_sets_has_more_and_total()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS public.page_users (
                  id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                  email text NOT NULL
                );
                DELETE FROM public.page_users;
                INSERT INTO public.page_users (email) VALUES ('a@x.com'), ('b@x.com'), ('c@x.com');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new PostgresDb(_fx.DataSource);
        var users = new PageUsers();
        var page = await db.Select(users.Email)
            .From(users.ToFrom())
            .OrderBy(users.Email.ToRef())
            .Page(1, 2)
            .ToPageAsync(r => r.GetString(0), includeTotal: true);

        Assert.Equal(["a@x.com", "b@x.com"], page.Items);
        Assert.True(page.HasMore);
        Assert.Equal(3, page.TotalCount);
    }
}

file sealed class PageUsers : PgTable<PageUsers>
{
    public PageUsers() : base("page_users", "public", "pu") { }

    public PgColumn<int> Id { get; } = Identity("id");
    public PgColumn<string> Email { get; } = Text("email");
}
