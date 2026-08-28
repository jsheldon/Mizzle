using Mizzle.Cli.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace Mizzle.Cli.Commands;

internal sealed class VersionCommand : Command<VersionCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--verbose")]
        [Description("Show build metadata when it is available.")]
        public bool Verbose { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var assembly = typeof(VersionCommand).Assembly;
        var version = settings.Verbose
            ? VersionInfo.FullVersion(assembly)
            : VersionInfo.PackageVersion(assembly);

        AnsiConsole.MarkupLine($"[bold]Mizzle CLI[/] {ConsoleText.Escape(version)}");
        return 0;
    }
}
