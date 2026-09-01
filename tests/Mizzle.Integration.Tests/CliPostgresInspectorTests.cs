using Mizzle.Cli.Schema;

namespace Mizzle.Integration.Tests;

public sealed class CliPostgresInspectorTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fx;

    public CliPostgresInspectorTests(PostgresFixture fx) => _fx = fx;

    [DockerFact]
    public async Task Postgres_inspector_reads_table_columns_and_key_metadata()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                DROP TABLE IF EXISTS public.cli_posts;
                CREATE TABLE public.cli_posts (
                  id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                  title varchar(120) NOT NULL,
                  body text NULL,
                  published boolean NOT NULL,
                  public_id uuid NOT NULL,
                  created_at timestamptz NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var inspector = new PostgresInspector();
        var tables = await inspector.InspectAsync(
            _fx.ConnectionString,
            "public",
            ["cli_posts"],
            CancellationToken.None);

        var table = Assert.Single(tables);
        Assert.Equal("public", table.Schema);
        Assert.Equal("cli_posts", table.Name);

        var id = Assert.Single(table.Columns, c => c.Name == "id");
        Assert.True(id.IsPrimaryKey);
        Assert.True(id.IsIdentity);
        Assert.False(id.IsNullable);

        var title = Assert.Single(table.Columns, c => c.Name == "title");
        Assert.Equal("character varying", title.StoreType);
        Assert.Equal(120, title.Length);
        Assert.False(title.IsNullable);

        var body = Assert.Single(table.Columns, c => c.Name == "body");
        Assert.True(body.IsNullable);

        Assert.Contains(table.Columns, c => c.Name == "published" && c.NativeType == "bool");
        Assert.Contains(table.Columns, c => c.Name == "public_id" && c.NativeType == "uuid");
        Assert.Contains(table.Columns, c => c.Name == "created_at" && c.NativeType == "timestamptz");
    }

    [DockerFact]
    public async Task Table_filter_is_case_insensitive()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                DROP TABLE IF EXISTS public.cli_widgets;
                CREATE TABLE public.cli_widgets (id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var inspector = new PostgresInspector();
        var tables = await inspector.InspectAsync(
            _fx.ConnectionString,
            "public",
            ["CLI_WIDGETS"],
            CancellationToken.None);

        var table = Assert.Single(tables);
        Assert.Equal("cli_widgets", table.Name);
    }

    [DockerFact]
    public async Task Requested_missing_table_is_reported_clearly()
    {
        var inspector = new PostgresInspector();
        var tables = await inspector.InspectAsync(
            _fx.ConnectionString,
            "public",
            ["definitely_not_a_real_mizzle_table"],
            CancellationToken.None);

        var ex = Assert.Throws<Mizzle.Cli.Infrastructure.CliFailure>(() =>
            Mizzle.Cli.Commands.SettingsHelpers.EnsureRequestedTablesFound(
                ["definitely_not_a_real_mizzle_table"],
                tables));

        Assert.Equal("MZCLI015", ex.Code);
        Assert.Contains("definitely_not_a_real_mizzle_table", ex.Message, StringComparison.Ordinal);
    }
}
