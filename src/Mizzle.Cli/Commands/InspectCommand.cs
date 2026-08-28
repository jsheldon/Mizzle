using Mizzle.Cli.Infrastructure;
using Mizzle.Cli.Schema;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Mizzle.Cli.Commands;

internal sealed class InspectCommand : AsyncCommand<InspectCommand.Settings>
{
    public sealed class Settings : DatabaseSettings;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var provider = ProviderKindParser.ParseOrInfer(settings.Provider, settings.Connection);
        var tables = SettingsHelpers.ParseTables(settings.Tables);
        if (!settings.All && tables is null)
        {
            throw new CliFailure("MZCLI010", "Choose --tables or --all.", "Scaffolding every table should be explicit.");
        }

        var info = await DatabaseInspector.For(provider).InspectAsync(settings.Connection, settings.Schema, tables, cancellationToken);
        if (info.Count == 0)
        {
            SettingsHelpers.EnsureRequestedTablesFound(tables, info);
            throw new CliFailure("MZCLI013", "No tables matched the supplied filters.");
        }

        foreach (var tableInfo in info)
        {
            AnsiConsole.MarkupLine($"[bold]{ConsoleText.Escape(tableInfo.Schema)}.{ConsoleText.Escape(tableInfo.Name)}[/]");
            var table = new Table().RoundedBorder();
            table.AddColumn("Column");
            table.AddColumn("Store type");
            table.AddColumn("Mizzle");
            table.AddColumn("Null");
            table.AddColumn("Key");
            foreach (var column in tableInfo.Columns)
            {
                string mizzle;
                try
                {
                    var mapping = TypeMappings.Resolve(provider, column);
                    mizzle = mapping.Factory;
                }
                catch (CliFailure ex)
                {
                    mizzle = $"[red]{ex.Code}[/]";
                }

                table.AddRow(
                    ConsoleText.Escape(column.Name),
                    ConsoleText.Escape(column.NativeType ?? column.StoreType),
                    mizzle,
                    column.IsNullable ? "yes" : "no",
                    column.IsPrimaryKey ? "PK" : "");
            }

            AnsiConsole.Write(table);
        }

        var missing = SettingsHelpers.MissingRequestedTables(tables, info);
        if (missing.Count > 0)
        {
            throw new CliFailure(
                "MZCLI015",
                $"Some requested tables were not found: {string.Join(", ", missing)}.",
                "Check the table names, --schema value, and database connection.");
        }

        return 0;
    }
}
