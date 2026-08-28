using Mizzle.Cli.Infrastructure;
using Mizzle.Cli.Schema;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Mizzle.Cli.Commands;

internal sealed class TypeMapCommand : Command<TypeMapCommand.Settings>
{
    public sealed class Settings : ProviderSettings;

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var provider = ProviderKindParser.Parse(settings.Provider);
        var table = new Table().RoundedBorder();
        table.AddColumn("Store type");
        table.AddColumn("C# type");
        table.AddColumn("Factory");
        foreach (var mapping in TypeMappings.For(provider))
        {
            table.AddRow(
                ConsoleText.Escape(mapping.StoreType),
                ConsoleText.Escape(mapping.ClrType),
                ConsoleText.Escape(mapping.NeedsLength ? $"{mapping.Factory}(name, length)" : $"{mapping.Factory}(name)"));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
