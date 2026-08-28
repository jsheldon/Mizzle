using Mizzle.Cli.Generation;
using Mizzle.Cli.Infrastructure;
using Mizzle.Cli.Schema;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace Mizzle.Cli.Commands;

internal sealed class ScaffoldCommand : AsyncCommand<ScaffoldCommand.Settings>
{
    public sealed class Settings : DatabaseSettings
    {
        [CommandOption("--namespace <NAMESPACE>")]
        public string Namespace { get; init; } = "";

        [CommandOption("--output <OUTPUT>")]
        public string Output { get; init; } = "";

        [CommandOption("--overwrite")]
        [Description("Overwrite existing generated files.")]
        public bool Overwrite { get; init; }

        [CommandOption("--dry-run")]
        [Description("Print files instead of writing them.")]
        public bool DryRun { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var provider = ProviderKindParser.ParseOrInfer(settings.Provider, settings.Connection);
        var tables = SettingsHelpers.ParseTables(settings.Tables);
        if (!settings.All && tables is null)
        {
            throw new CliFailure("MZCLI010", "Choose --tables or --all.", "Generating every table should be explicit.");
        }

        if (string.IsNullOrWhiteSpace(settings.Namespace))
        {
            throw new CliFailure("MZCLI011", "Missing --namespace.");
        }

        if (!settings.DryRun && string.IsNullOrWhiteSpace(settings.Output))
        {
            throw new CliFailure("MZCLI012", "Missing --output.", "Use --dry-run to print generated code instead.");
        }

        var inspected = await DatabaseInspector.For(provider).InspectAsync(settings.Connection, settings.Schema, tables, cancellationToken);
        SettingsHelpers.EnsureRequestedTablesFound(tables, inspected);
        if (inspected.Count == 0)
        {
            throw new CliFailure("MZCLI013", "No tables matched the supplied filters.");
        }

        foreach (var table in inspected)
        {
            var file = TableClassWriter.Write(provider, settings.Namespace, table);
            if (settings.DryRun)
            {
                AnsiConsole.WriteLine($"// {file.FileName}");
                AnsiConsole.WriteLine(file.Source);
                continue;
            }

            Directory.CreateDirectory(settings.Output);
            var path = Path.Combine(settings.Output, file.FileName);
            if (File.Exists(path) && !settings.Overwrite)
            {
                throw new CliFailure("MZCLI014", $"Refusing to overwrite '{path}'.", "Pass --overwrite to replace existing generated files.");
            }

            await File.WriteAllTextAsync(path, file.Source);
            AnsiConsole.MarkupLineInterpolated($"[green]wrote[/] {path}");
        }

        return 0;
    }
}
