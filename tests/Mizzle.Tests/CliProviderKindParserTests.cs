using Mizzle.Cli.Infrastructure;

namespace Mizzle.Tests;

public sealed class CliProviderKindParserTests
{
    [Fact]
    public void Parses_provider_aliases()
    {
        Assert.Equal(ProviderKind.Postgres, ProviderKindParser.Parse("postgres"));
        Assert.Equal(ProviderKind.Postgres, ProviderKindParser.Parse("postgresql"));
        Assert.Equal(ProviderKind.Postgres, ProviderKindParser.Parse("pg"));
        Assert.Equal(ProviderKind.SqlServer, ProviderKindParser.Parse("sqlserver"));
        Assert.Equal(ProviderKind.SqlServer, ProviderKindParser.Parse("mssql"));
    }

    [Fact]
    public void Unknown_provider_fails_with_clear_code()
    {
        var ex = Assert.Throws<CliFailure>(() => ProviderKindParser.Parse("oracle"));

        Assert.Equal("MZCLI001", ex.Code);
        Assert.Contains("Use 'postgres' or 'sqlserver'", ex.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_provider_fails_with_clear_code()
    {
        var ex = Assert.Throws<CliFailure>(() => ProviderKindParser.Parse(""));

        Assert.Equal("MZCLI001", ex.Code);
        Assert.Contains("Missing --provider", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Infers_postgres_from_connection_string()
    {
        Assert.Equal(ProviderKind.Postgres, ProviderKindParser.ParseOrInfer("", "Host=localhost;Database=app;Username=postgres"));
        Assert.Equal(ProviderKind.Postgres, ProviderKindParser.ParseOrInfer(null, "postgres://user:pass@localhost/app"));
    }

    [Fact]
    public void Infers_sql_server_from_connection_string()
    {
        Assert.Equal(ProviderKind.SqlServer, ProviderKindParser.ParseOrInfer("", "Server=localhost;Database=app;User Id=sa"));
        Assert.Equal(ProviderKind.SqlServer, ProviderKindParser.ParseOrInfer(null, "Data Source=localhost;Initial Catalog=app;Integrated Security=true"));
    }

    [Fact]
    public void Ambiguous_connection_string_requires_provider()
    {
        var ex = Assert.Throws<CliFailure>(() => ProviderKindParser.ParseOrInfer("", "Database=app"));

        Assert.Equal("MZCLI003", ex.Code);
        Assert.Contains("--provider postgres", ex.Hint, StringComparison.Ordinal);
    }
}
