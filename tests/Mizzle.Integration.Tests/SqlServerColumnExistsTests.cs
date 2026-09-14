using Mizzle.SqlServer;

namespace Mizzle.Integration.Tests;

public sealed class SqlServerColumnExistsTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fx;

    public SqlServerColumnExistsTests(SqlServerFixture fx) => _fx = fx;

    [DockerFact]
    public async Task Returns_true_for_a_column_that_exists()
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
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new SqlDb(_fx.DataSource);
        var users = new SqlServerColumnExistsUsers();

        Assert.True(await db.ColumnExistsAsync(users, users.Email));
    }

    [DockerFact]
    public async Task Returns_false_for_a_column_that_does_not_exist()
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
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new SqlDb(_fx.DataSource);
        var users = new SqlServerColumnExistsUsers();

        Assert.False(await db.ColumnExistsAsync(users, users.NotAColumn));
    }

    [DockerFact]
    public async Task Uses_the_tables_own_name_not_its_alias()
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
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new SqlDb(_fx.DataSource);
        var aliased = new SqlServerColumnExistsUsers().WithAlias("u");

        Assert.True(await db.ColumnExistsAsync(aliased, aliased.Email));
    }

    [DockerFact]
    public async Task ColumnsExistAsync_returns_only_the_pairs_that_exist_in_one_round_trip()
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
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new SqlDb(_fx.DataSource);
        var users = new SqlServerColumnExistsUsers();

        var found = await db.ColumnsExistAsync(
        [
            (users, users.Email),
            (users, users.Id),
            (users, users.NotAColumn),
        ]);

        Assert.Equal(2, found.Count);
        Assert.Contains(("users", "email"), found);
        Assert.Contains(("users", "id"), found);
        Assert.DoesNotContain(("users", "not_a_column"), found);
    }

    [DockerFact]
    public async Task ColumnsExistAsync_returns_empty_for_an_empty_check_list()
    {
        var db = new SqlDb(_fx.DataSource);

        var found = await db.ColumnsExistAsync([]);

        Assert.Empty(found);
    }

    [DockerFact]
    public async Task ColumnsExistAsync_works_with_exactly_one_check()
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
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new SqlDb(_fx.DataSource);
        var users = new SqlServerColumnExistsUsers();

        var found = await db.ColumnsExistAsync([(users, users.Email)]);

        Assert.Single(found);
        Assert.Contains(("users", "email"), found);
    }
}

file sealed class SqlServerColumnExistsUsers : SqlTable<SqlServerColumnExistsUsers>
{
    public SqlServerColumnExistsUsers() : base("users", "dbo") { }

    public SqlColumn<int> Id { get; } = Identity("id");
    public SqlColumn<string> Email { get; } = NVarChar("email", 255);
    public SqlColumn<string> NotAColumn { get; } = NVarChar("not_a_column", 255);
}
