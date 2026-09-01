using Mizzle.Cli.Schema;

namespace Mizzle.Integration.Tests;

public sealed class CliSqlServerInspectorTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fx;

    public CliSqlServerInspectorTests(SqlServerFixture fx) => _fx = fx;

    [DockerFact]
    public async Task Sql_server_inspector_reads_table_columns_and_key_metadata()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                IF OBJECT_ID(N'dbo.cli_posts', N'U') IS NOT NULL DROP TABLE dbo.cli_posts;
                CREATE TABLE dbo.cli_posts (
                  id int IDENTITY(1,1) PRIMARY KEY,
                  title nvarchar(120) NOT NULL,
                  body nvarchar(max) NULL,
                  published bit NOT NULL,
                  public_id uniqueidentifier NOT NULL,
                  created_at datetime2 NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var inspector = new SqlServerInspector();
        var tables = await inspector.InspectAsync(
            _fx.ConnectionString,
            "dbo",
            ["cli_posts"],
            CancellationToken.None);

        var table = Assert.Single(tables);
        Assert.Equal("dbo", table.Schema);
        Assert.Equal("cli_posts", table.Name);

        var id = Assert.Single(table.Columns, c => c.Name == "id");
        Assert.True(id.IsPrimaryKey);
        Assert.True(id.IsIdentity);
        Assert.False(id.IsNullable);

        var title = Assert.Single(table.Columns, c => c.Name == "title");
        Assert.Equal("nvarchar", title.StoreType);
        Assert.Equal(120, title.Length);
        Assert.False(title.IsNullable);

        var body = Assert.Single(table.Columns, c => c.Name == "body");
        Assert.True(body.IsNullable);

        Assert.Contains(table.Columns, c => c.Name == "published" && c.StoreType == "bit");
        Assert.Contains(table.Columns, c => c.Name == "public_id" && c.StoreType == "uniqueidentifier");
        Assert.Contains(table.Columns, c => c.Name == "created_at" && c.StoreType == "datetime2");
    }

    [DockerFact]
    public async Task Table_filter_is_case_insensitive()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                IF OBJECT_ID(N'dbo.cli_widgets', N'U') IS NOT NULL DROP TABLE dbo.cli_widgets;
                CREATE TABLE dbo.cli_widgets (id int IDENTITY(1,1) PRIMARY KEY);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var inspector = new SqlServerInspector();
        var tables = await inspector.InspectAsync(
            _fx.ConnectionString,
            "dbo",
            ["CLI_WIDGETS"],
            CancellationToken.None);

        var table = Assert.Single(tables);
        Assert.Equal("cli_widgets", table.Name);
    }
}
