using Mizzle.SqlServer;

namespace Mizzle.Integration.Tests;

public sealed class SqlServerSelectTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fx;

    public SqlServerSelectTests(SqlServerFixture fx) => _fx = fx;

    [DockerFact]
    public async Task Selects_inserted_email()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                IF OBJECT_ID(N'dbo.users', N'U') IS NULL
                BEGIN
                  CREATE TABLE dbo.users (
                    id int IDENTITY(1,1) PRIMARY KEY,
                    email nvarchar(255) NOT NULL
                  );
                END
                DELETE FROM dbo.users;
                INSERT INTO dbo.users (email) VALUES (N'a@b.com');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new SqlDb(_fx.DataSource);
        var users = new SqlUsers();
        var rows = await db.Select(users.Email)
            .From(users.ToFrom())
            .Where(users.Email, "a@b.com")
            .ToListAsync(r => r.GetString(0));

        Assert.Equal(["a@b.com"], rows);
    }
}

file sealed class SqlUsers : SqlTable<SqlUsers>
{
    public SqlUsers() : base("users", "dbo") { }

    public SqlColumn<int> Id { get; } = Identity("id");
    public SqlColumn<string> Email { get; } = NVarChar("email", 255);
}
