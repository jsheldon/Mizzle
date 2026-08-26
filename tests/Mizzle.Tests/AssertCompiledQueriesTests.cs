using Npgsql;

namespace Mizzle.Tests;

file sealed class AssertUsers : PgTable<AssertUsers>
{
    public AssertUsers() : base("users", "public", "u") { }

    public PgColumn<string> Email { get; } = Text("email");
}

public sealed class AssertCompiledQueriesTests
{
    [Fact]
    public async Task Throws_when_query_was_not_interceptable()
    {
        await using var dataSource = NpgsqlDataSource.Create("Host=localhost;Username=x;Password=y;Database=z");
        var db = new PostgresDb(dataSource, new MizzleOptions { AssertCompiledQueries = true });
        var users = new AssertUsers();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.Select(users.Email).From(users.ToFrom()).ToListAsync(r => r.GetString(0)));
        Assert.Equal("Query was not interceptable", ex.Message);
    }

    [Fact]
    public async Task Runtime_write_does_not_assert()
    {
        // Writes are not interceptable in phase 1 by design; the assert must not
        // fire on them. The call still fails (no server) — but with a connection
        // error, never the assert exception.
        await using var dataSource = NpgsqlDataSource.Create(
            "Host=localhost;Port=1;Username=x;Password=y;Database=z;Timeout=1");
        var db = new PostgresDb(dataSource, new MizzleOptions { AssertCompiledQueries = true });
        var users = new AssertUsers();
        var ex = await Record.ExceptionAsync(
            () => db.InsertInto(users).Value(users.Email, "x").ExecuteAsync());
        Assert.NotNull(ex);
        Assert.False(ex is InvalidOperationException { Message: "Query was not interceptable" });
    }
}
