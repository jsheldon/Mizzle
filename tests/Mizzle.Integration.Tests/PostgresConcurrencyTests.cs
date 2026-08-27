namespace Mizzle.Integration.Tests;

public sealed class PostgresConcurrencyTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fx;

    public PostgresConcurrencyTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Expect_mismatch_throws_when_no_rows_updated()
    {
        await EnsureExpectUsersTable();
        var db = new PostgresDb(_fx.DataSource);
        var users = new ExpectUsers();
        var ex = await Assert.ThrowsAsync<ConcurrencyException>(() =>
            db.Update(users)
                .Set(users.Email, "nobody@x.com")
                .Where(users.Id, -1)
                .Expect(1)
                .ExecuteAsync());
        Assert.Equal(1, ex.Expected);
        Assert.Equal(0, ex.Actual);
    }

    [Fact]
    public async Task Expect_succeeds_when_one_row_updated()
    {
        await EnsureExpectUsersTable();
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                DELETE FROM public.expect_users;
                INSERT INTO public.expect_users (email) VALUES ('old@x.com');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new PostgresDb(_fx.DataSource);
        var users = new ExpectUsers();
        var id = await db.Select(users.Id).From(users.ToFrom()).FirstAsync(r => r.GetInt32(0));
        var affected = await db.Update(users)
            .Set(users.Email, "new@x.com")
            .Where(users.Id, id)
            .Expect(1)
            .ExecuteAsync();
        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task LockAsync_is_reentrant_on_same_transaction()
    {
        var db = new PostgresDb(_fx.DataSource);
        await db.Transaction(async tx =>
        {
            await tx.LockAsync("k");
            await tx.LockAsync("k");
        });
    }

    private async Task EnsureExpectUsersTable()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS public.expect_users (
              id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
              email text NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}

file sealed class ExpectUsers : PgTable<ExpectUsers>
{
    public ExpectUsers() : base("expect_users", "public") { }

    public PgColumn<int> Id { get; } = Identity("id");
    public PgColumn<string> Email { get; } = Text("email");
}
