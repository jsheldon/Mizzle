using Mizzle.Schema;

namespace Mizzle.Integration.Tests;

public sealed class PostgresSelectTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fx;

    public PostgresSelectTests(PostgresFixture fx) => _fx = fx;

    [DockerFact]
    public async Task Selects_inserted_email()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS public.users (
                  id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                  email text NOT NULL
                );
                DELETE FROM public.users;
                INSERT INTO public.users (email) VALUES ('a@b.com');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new PostgresDb(_fx.DataSource);
        var users = new Users();
        var rows = await db.Select(users.Email)
            .From(users.ToFrom())
            .Where(users.Email, "a@b.com")
            .ToListAsync(r => r.GetString(0));

        Assert.Equal(["a@b.com"], rows);
    }

    [DockerFact]
    public async Task FirstAsync_throws_when_empty()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS public.users (
                  id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                  email text NOT NULL
                );
                DELETE FROM public.users;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new PostgresDb(_fx.DataSource);
        var users = new Users();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.Select(users.Email).From(users.ToFrom()).FirstAsync(r => r.GetString(0)));
    }

    [DockerFact]
    public async Task SingleAsync_throws_when_two_rows()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS public.users (
                  id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                  email text NOT NULL
                );
                DELETE FROM public.users;
                INSERT INTO public.users (email) VALUES ('a@b.com'), ('b@c.com');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new PostgresDb(_fx.DataSource);
        var users = new Users();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.Select(users.Email).From(users.ToFrom()).SingleAsync(r => r.GetString(0)));
    }

    [DockerFact]
    public async Task ToAsyncEnumerable_yields_rows()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS public.users (
                  id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                  email text NOT NULL
                );
                DELETE FROM public.users;
                INSERT INTO public.users (email) VALUES ('a@b.com'), ('b@c.com');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new PostgresDb(_fx.DataSource);
        var users = new Users();
        var rows = new List<string>();
        await foreach (var email in db.Select(users.Email).From(users.ToFrom()).OrderBy(users.Email.ToRef())
                           .ToAsyncEnumerable(r => r.GetString(0)))
        {
            rows.Add(email);
        }

        Assert.Equal(["a@b.com", "b@c.com"], rows);
    }
}
