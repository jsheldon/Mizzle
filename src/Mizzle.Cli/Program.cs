using Mizzle.Cli.Commands;
using Mizzle.Cli.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("mizzle");
    config.ValidateExamples();
    config.PropagateExceptions();

    config.AddCommand<TypeMapCommand>("type-map")
        .WithDescription("Show database type mappings Mizzle can scaffold.");
    config.AddCommand<TypeMapCommand>("typemap")
        .WithDescription("Show database type mappings Mizzle can scaffold.");
    config.AddCommand<InspectCommand>("inspect")
        .WithDescription("Inspect schemas, tables, columns, and unsupported types.");
    config.AddCommand<ScaffoldCommand>("scaffold")
        .WithDescription("Generate Mizzle table classes from a database.");
    config.AddCommand<DoctorCommand>("doctor")
        .WithDescription("Check a project for common Mizzle setup issues.");
    config.AddCommand<DiffCommand>("diff")
        .WithDescription("Compare generated table classes against a live database.");
    config.AddCommand<ExplainCommand>("explain")
        .WithDescription("Explain SQL features and likely Mizzle support.");
    config.AddCommand<TranslateQueryCommand>("translate-query")
        .WithDescription("Translate a small SQL subset into Mizzle query syntax.");
    config.AddCommand<VersionCommand>("version")
        .WithDescription("Show the Mizzle CLI version.");
});

try
{
    return await app.RunAsync(args);
}
catch (CliFailure ex)
{
    AnsiConsole.MarkupLine($"[red]{ConsoleText.Escape(ex.Code)}[/] {ConsoleText.Escape(ex.Message)}");
    if (!string.IsNullOrWhiteSpace(ex.Hint))
    {
        AnsiConsole.MarkupLine($"[grey]{ConsoleText.Escape(ex.Hint)}[/]");
    }

    return ex.ExitCode;
}
catch (CommandParseException ex)
{
    AnsiConsole.MarkupLine($"[red]MZCLI000[/] {ConsoleText.Escape(ex.Message)}");
    AnsiConsole.MarkupLine("[grey]Run 'mizzle --help' to see available commands.[/]");
    return 2;
}
catch (Exception ex)
{
    if (ConnectionFailureTranslator.TryTranslate(ex) is { } failure)
    {
        AnsiConsole.MarkupLine($"[red]{ConsoleText.Escape(failure.Code)}[/] {ConsoleText.Escape(failure.Message)}");
        if (!string.IsNullOrWhiteSpace(failure.Hint))
        {
            AnsiConsole.MarkupLine($"[grey]{ConsoleText.Escape(failure.Hint)}[/]");
        }

        return failure.ExitCode;
    }

    AnsiConsole.MarkupLine($"[red]MZCLI999[/] Unexpected error: {ConsoleText.Escape(ex.Message)}");
    AnsiConsole.MarkupLine("[grey]Run again with the same arguments and file an issue if this repeats.[/]");
    return 1;
}
