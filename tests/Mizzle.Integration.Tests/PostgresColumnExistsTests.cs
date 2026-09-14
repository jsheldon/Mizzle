using Mizzle.Schema;

namespace Mizzle.Integration.Tests;

public sealed class PostgresColumnExistsTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fx;

    public PostgresColumnExistsTests(PostgresFixture fx) => _fx = fx;

    [DockerFact]
    public async Task Returns_true_for_a_column_that_exists()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS public.users (
                  id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                  email text NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new PostgresDb(_fx.DataSource);
        var users = new PostgresColumnExistsUsers();

        Assert.True(await db.ColumnExistsAsync(users, users.Email));
    }

    [DockerFact]
    public async Task Returns_false_for_a_column_that_does_not_exist()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS public.users (
                  id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                  email text NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new PostgresDb(_fx.DataSource);
        var users = new PostgresColumnExistsUsers();

        Assert.False(await db.ColumnExistsAsync(users, users.NotAColumn));
    }

    [DockerFact]
    public async Task Uses_the_tables_own_name_not_its_alias()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS public.users (
                  id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                  email text NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new PostgresDb(_fx.DataSource);
        var aliased = new PostgresColumnExistsUsers().WithAlias("u");

        Assert.True(await db.ColumnExistsAsync(aliased, aliased.Email));
    }
}

file sealed class PostgresColumnExistsUsers : PgTable<PostgresColumnExistsUsers>
{
    public PostgresColumnExistsUsers() : base("users", "public") { }

    public PgColumn<int> Id { get; } = Identity("id");
    public PgColumn<string> Email { get; } = Text("email");
    public PgColumn<string> NotAColumn { get; } = Text("not_a_column");
}
