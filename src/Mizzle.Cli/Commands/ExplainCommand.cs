using Mizzle.Cli.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace Mizzle.Cli.Commands;

internal sealed class ExplainCommand : Command<ExplainCommand.Settings>
{
    public sealed class Settings : ProviderSettings
    {
        [CommandOption("--sql <SQL>")]
        public string? Sql { get; init; }

        [CommandOption("--sql-file <FILE>")]
        public string? SqlFile { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        _ = ProviderKindParser.Parse(settings.Provider);
        var sql = ReadSql(settings.Sql, settings.SqlFile);
        var lower = sql.ToLowerInvariant();
        var table = new Table().RoundedBorder();
        table.AddColumn("Feature");
        table.AddColumn("Status");
        Add(table, "SELECT/FROM", lower.Contains("select ") && lower.Contains(" from "));
        Add(table, "JOIN", lower.Contains(" join "));
        Add(table, "WHERE", lower.Contains(" where "));
        Add(table, "ORDER BY", lower.Contains(" order by "));
        Add(table, "LIMIT/OFFSET", lower.Contains(" limit ") || lower.Contains(" offset "));
        Add(table, "GROUP BY", lower.Contains(" group by "), supported: false);
        Add(table, "CTE", lower.Contains("with "), supported: false);
        Add(table, "Window functions", lower.Contains(" over(") || lower.Contains(" over ("), supported: false);
        AnsiConsole.Write(table);
        return 0;
    }

    internal static string ReadSql(string? sql, string? file)
    {
        if (!string.IsNullOrWhiteSpace(sql))
        {
            return sql;
        }

        if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
        {
            return File.ReadAllText(file);
        }

        throw new CliFailure("MZCLI050", "Provide --sql or --sql-file.");
    }

    private static void Add(Table table, string feature, bool present, bool supported = true)
        => table.AddRow(feature, !present ? "[grey]not present[/]" : supported ? "[green]recognized[/]" : "[red]not translatable yet[/]");
}
