using Mizzle.Cli.Infrastructure;
using Mizzle.Cli.Schema;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace Mizzle.Cli.Commands;

internal abstract class ProviderSettings : CommandSettings
{
    [CommandOption("--provider <PROVIDER>")]
    [Description("Database provider: postgres or sqlserver. Database commands infer this when possible.")]
    public string Provider { get; init; } = "";
}

internal abstract class DatabaseSettings : ProviderSettings
{
    [CommandOption("--connection <CONNECTION>")]
    [Description("Database connection string.")]
    public string Connection { get; init; } = "";

    [CommandOption("--schema <SCHEMA>")]
    [Description("Schema to inspect. Defaults to all user schemas.")]
    public string? Schema { get; init; }

    [CommandOption("--tables <TABLES>")]
    [Description("Comma-separated table names. Omit with --all to include every table in the schema.")]
    public string? Tables { get; init; }

    [CommandOption("--all")]
    [Description("Include all tables in the selected schema.")]
    public bool All { get; init; }
}

internal static class SettingsHelpers
{
    public static IReadOnlyList<string>? ParseTables(string? tables)
        => string.IsNullOrWhiteSpace(tables)
            ? null
            : [.. tables.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public static void EnsureRequestedTablesFound(IReadOnlyList<string>? requested, IReadOnlyList<TableInfo> found)
    {
        if (requested is null)
        {
            return;
        }

        var missing = MissingRequestedTables(requested, found);
        if (missing.Count == 0)
        {
            return;
        }

        throw new CliFailure(
            "MZCLI015",
            missing.Count == requested.Count
                ? $"None of the requested tables were found: {string.Join(", ", missing)}."
                : $"Some requested tables were not found: {string.Join(", ", missing)}.",
            "Check the table names, --schema value, and database connection.");
    }

    public static IReadOnlyList<string> MissingRequestedTables(IReadOnlyList<string>? requested, IReadOnlyList<TableInfo> found)
    {
        if (requested is null)
        {
            return [];
        }

        var foundNames = found.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. requested.Where(t => !foundNames.Contains(t))];
    }
}
