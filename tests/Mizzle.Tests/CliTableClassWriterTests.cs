using Mizzle.Cli.Generation;
using Mizzle.Cli.Infrastructure;
using Mizzle.Cli.Schema;

namespace Mizzle.Tests;

public sealed class CliTableClassWriterTests
{
    [Fact]
    public void Writes_postgres_table_class_with_supported_types_and_modifiers()
    {
        var table = new TableInfo(
            "public",
            "users",
            [
                new ColumnInfo("public", "users", "id", "integer", "int4", null, false, true, true),
                new ColumnInfo("public", "users", "email", "text", "text", null, false, false, false),
                new ColumnInfo("public", "users", "display_name", "character varying", "varchar", 120, true, false, false),
                new ColumnInfo("public", "users", "created_at", "timestamp with time zone", "timestamptz", null, false, false, false),
            ]);

        var file = TableClassWriter.Write(ProviderKind.Postgres, "Demo.Data", table);

        Assert.Equal("Users.cs", file.FileName);
        Assert.Contains("using Mizzle.Postgres;", file.Source, StringComparison.Ordinal);
        Assert.Contains("namespace Demo.Data;", file.Source, StringComparison.Ordinal);
        Assert.Contains("public sealed class Users : PgTable<Users>", file.Source, StringComparison.Ordinal);
        Assert.Contains("public Users() : base(\"users\", \"public\")", file.Source, StringComparison.Ordinal);
        Assert.Contains("public PgColumn<int> Id { get; } = Identity(\"id\").PrimaryKey();", file.Source, StringComparison.Ordinal);
        Assert.Contains("public PgColumn<string> Email { get; } = Text(\"email\").NotNull();", file.Source, StringComparison.Ordinal);
        Assert.Contains("public PgColumn<string> DisplayName { get; } = Varchar(\"display_name\", 120);", file.Source, StringComparison.Ordinal);
        Assert.Contains("public PgColumn<DateTimeOffset> CreatedAt { get; } = Timestamptz(\"created_at\").NotNull();", file.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_sql_server_nvarchar_max_when_length_is_unknown()
    {
        var table = new TableInfo(
            "dbo",
            "notes",
            [
                new ColumnInfo("dbo", "notes", "body", "nvarchar", "nvarchar", null, true, false, false),
            ]);

        var file = TableClassWriter.Write(ProviderKind.SqlServer, "Demo.Data", table);

        Assert.Contains("public sealed class Notes : SqlTable<Notes>", file.Source, StringComparison.Ordinal);
        Assert.Contains("public SqlColumn<string> Body { get; } = NVarCharMax(\"body\");", file.Source, StringComparison.Ordinal);
    }
}
