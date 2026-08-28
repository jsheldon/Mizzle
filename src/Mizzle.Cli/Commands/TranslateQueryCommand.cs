using Mizzle.Cli.Infrastructure;
using Mizzle.Cli.SqlTranslation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Mizzle.Cli.Commands;

internal sealed class TranslateQueryCommand : Command<TranslateQueryCommand.Settings>
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
        var provider = ProviderKindParser.Parse(settings.Provider);
        AnsiConsole.Write(SqlTranslator.Translate(provider, ExplainCommand.ReadSql(settings.Sql, settings.SqlFile)));
        return 0;
    }
}
