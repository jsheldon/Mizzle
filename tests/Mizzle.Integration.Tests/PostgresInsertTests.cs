namespace Mizzle.Integration.Tests;

public sealed class PostgresInsertTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fx;

    public PostgresInsertTests(PostgresFixture fx) => _fx = fx;

    [DockerFact]
    public async Task Insert_returning_maps_typed_record()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS public.insert_users (
                  id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                  email text NOT NULL
                );
                DELETE FROM public.insert_users;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new PostgresDb(_fx.DataSource);
        var users = new InsertUsers();
        var inserted = await db.InsertInto(users)
            .Value(users.Email, "a@b.com")
            .Returning(users.Id, users.Email)
            .SingleAsync<InsertedUserRow>();

        Assert.True(inserted.Id > 0);
        Assert.Equal("a@b.com", inserted.Email);
    }
}

file sealed class InsertUsers : PgTable<InsertUsers>
{
    public InsertUsers() : base("insert_users", "public") { }

    public PgColumn<int> Id { get; } = Identity("id");
    public PgColumn<string> Email { get; } = Text("email").NotNull();
}

file sealed record InsertedUserRow(int Id, string Email);
