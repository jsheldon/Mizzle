namespace Mizzle.Cli.Infrastructure;

internal enum ProviderKind
{
    Postgres,
    SqlServer,
}

internal static class ProviderKindParser
{
    public static ProviderKind ParseOrInfer(string? provider, string connectionString)
    {
        if (!string.IsNullOrWhiteSpace(provider))
        {
            return Parse(provider);
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new CliFailure(
                "MZCLI004",
                "Missing --connection.",
                "Pass a database connection string.");
        }

        var value = connectionString.Trim();
        var lower = value.ToLowerInvariant();
        var postgres = lower.StartsWith("postgres://", StringComparison.Ordinal)
            || lower.StartsWith("postgresql://", StringComparison.Ordinal)
            || HasKey(lower, "host")
            || HasKey(lower, "username");
        var sqlServer = HasKey(lower, "server")
            || HasKey(lower, "data source")
            || HasKey(lower, "initial catalog")
            || HasKey(lower, "integrated security")
            || HasKey(lower, "trustservercertificate");

        return (postgres, sqlServer) switch
        {
            (true, false) => ProviderKind.Postgres,
            (false, true) => ProviderKind.SqlServer,
            _ => throw new CliFailure(
                "MZCLI003",
                "Could not infer provider from the connection string.",
                "Pass '--provider postgres' or '--provider sqlserver'.")
        };
    }

    public static ProviderKind Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliFailure(
                "MZCLI001",
                "Missing --provider.",
                "Use '--provider postgres' or '--provider sqlserver'.");
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "postgres" or "postgresql" or "pg" => ProviderKind.Postgres,
            "sqlserver" or "sql-server" or "mssql" or "sql" => ProviderKind.SqlServer,
            _ => throw new CliFailure(
                "MZCLI001",
                $"Unsupported provider '{value}'.",
                "Use 'postgres' or 'sqlserver'.")
        };
    }

    private static bool HasKey(string connectionString, string key)
        => connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => part.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
}
