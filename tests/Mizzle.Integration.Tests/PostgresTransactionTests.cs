namespace Mizzle.Integration.Tests;

public sealed class PostgresTransactionTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fx;

    public PostgresTransactionTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Timeout_overlay_does_not_mutate_global_options()
    {
        var options = new MizzleOptions { CommandTimeout = TimeSpan.FromSeconds(30) };
        var db = new PostgresDb(_fx.DataSource, options);
        var users = new Users();
        var builder = db.Select(users.Email).Timeout(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(30), options.CommandTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), builder.Overlay!.CommandTimeout);
    }

    [Fact]
    public async Task Transaction_commits_insert()
    {
        await EnsureUsersTable();
        var db = new PostgresDb(_fx.DataSource);
        var users = new Users();
        var bag = new ParamBag();
        var email = bag.Add("tx@b.com", typeof(string));
        await db.Transaction(async tx =>
        {
            await tx.ExecuteAsync(
                new InsertQuery(users.ToFrom(), ["email"], [[email]], null, [], [], false),
                bag);
        });

        var rows = await db.Select(users.Email).From(users.ToFrom()).ToListAsync(r => r.GetString(0));
        Assert.Contains("tx@b.com", rows);
    }

    [Fact]
    public async Task Transaction_rolls_back_on_throw()
    {
        await EnsureUsersTable();
        var db = new PostgresDb(_fx.DataSource);
        var users = new Users();
        var bag = new ParamBag();
        var email = bag.Add("rollback@b.com", typeof(string));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await db.Transaction(async tx =>
            {
                await tx.ExecuteAsync(
                    new InsertQuery(users.ToFrom(), ["email"], [[email]], null, [], [], false),
                    bag);
                throw new InvalidOperationException("boom");
            });
        });

        var rows = await db.Select(users.Email).From(users.ToFrom()).ToListAsync(r => r.GetString(0));
        Assert.DoesNotContain("rollback@b.com", rows);
    }

    private async Task EnsureUsersTable()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS public.users (
              id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
              email text NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
