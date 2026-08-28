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
            if (!declared.TryGetValue(table.Name, out var columns))
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

    private static Dictionary<string, HashSet<string>> ParseDeclared(string source)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var table = Regex.Match(text, "base\\(\"(?<name>[^\"]+)\"");
            if (!table.Success)
            {
                continue;
            }

            var columns = Regex.Matches(text, "\\w+\\(\"(?<name>[^\"]+)\"")
                .Select(m => m.Groups["name"].Value)
                .Where(v => v != table.Groups["name"].Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            result[table.Groups["name"].Value] = columns;
        }

        return result;
    }
}
