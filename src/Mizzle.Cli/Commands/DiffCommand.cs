using Mizzle.Cli.Infrastructure;
using Mizzle.Cli.Schema;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace Mizzle.Cli.Commands;

internal sealed class DiffCommand : AsyncCommand<DiffCommand.Settings>
{
    public sealed class Settings : DatabaseSettings
    {
        [CommandOption("--source <SOURCE>")]
        [Description("Directory containing Mizzle table classes.")]
        public string Source { get; init; } = "";
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Source) || !Directory.Exists(settings.Source))
        {
            throw new CliFailure("MZCLI040", "Source directory not found.", "Pass --source ./Data/Tables.");
        }

        var provider = ProviderKindParser.ParseOrInfer(settings.Provider, settings.Connection);
        var requestedTables = SettingsHelpers.ParseTables(settings.Tables);
        var live = await DatabaseInspector.For(provider).InspectAsync(settings.Connection, settings.Schema, requestedTables, cancellationToken);
        SettingsHelpers.EnsureRequestedTablesFound(requestedTables, live);
        if (live.Count == 0)
        {
            throw new CliFailure("MZCLI013", "No tables matched the supplied filters.");
        }

        var declared = ParseDeclared(settings.Source);
        var hadDiff = false;
        foreach (var table in live)
        {
            if (!declared.TryGetValue((table.Schema, table.Name), out var columns)
                && !declared.TryGetValue(("", table.Name), out columns))
            {
                AnsiConsole.MarkupLineInterpolated($"[yellow]missing table class[/] {table.Schema}.{table.Name}");
                hadDiff = true;
                continue;
            }

            foreach (var column in table.Columns)
            {
                if (!columns.Contains(column.Name))
                {
                    AnsiConsole.MarkupLineInterpolated($"[yellow]missing column[/] {table.Schema}.{table.Name}.{column.Name}");
                    hadDiff = true;
                }
            }
        }

        if (!hadDiff)
        {
            AnsiConsole.MarkupLine("[green]No drift found in the checked tables.[/]");
        }

        return hadDiff ? 1 : 0;
    }

    internal static Dictionary<(string Schema, string Name), HashSet<string>> ParseDeclared(string source)
    {
        var result = new Dictionary<(string Schema, string Name), HashSet<string>>(TableKeyComparer.Instance);
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            // Table classes are generated as base("table") or base("table", "schema"); the schema
            // group is empty when a class was hand-written without one.
            var table = Regex.Match(text, "base\\(\\s*\"(?<name>[^\"]+)\"(?:\\s*,\\s*\"(?<schema>[^\"]+)\")?");
            if (!table.Success)
            {
                continue;
            }

            var name = table.Groups["name"].Value;
            var schema = table.Groups["schema"].Success ? table.Groups["schema"].Value : "";
            var columns = Regex.Matches(text, "(?<!\\.)\\b\\w+\\(\"(?<name>[^\"]+)\"")
                .Select(m => m.Groups["name"].Value)
                .Where(v => v != name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            result[(schema, name)] = columns;
        }

        return result;
    }

    private sealed class TableKeyComparer : IEqualityComparer<(string Schema, string Name)>
    {
        public static readonly TableKeyComparer Instance = new();

        public bool Equals((string Schema, string Name) x, (string Schema, string Name) y)
            => string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Schema, string Name) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Schema),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }
}
