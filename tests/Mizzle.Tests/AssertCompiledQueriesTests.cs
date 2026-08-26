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
}
