using Mizzle.Cli.Doctor;
using Mizzle.Cli.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace Mizzle.Cli.Commands;

internal sealed class DoctorCommand : Command<DoctorCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--project <PROJECT>")]
        [Description("Project file to inspect.")]
        public string? Project { get; init; }

        [CommandOption("--solution <SOLUTION>")]
        [Description("Solution file to inspect.")]
        public string? Solution { get; init; }

        [CommandOption("--all-projects")]
        [Description("Show projects that do not appear to use Mizzle.")]
        public bool AllProjects { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var target = DoctorTargetResolver.Resolve(settings.Project, settings.Solution, Environment.CurrentDirectory);
        var allReports = target.ProjectPaths
            .Select(path => ProjectDoctorAnalyzer.Analyze(ProjectDoctorReader.Read(path, target.Root)))
            .ToList();
        var reports = allReports
            .Where(report => settings.AllProjects || report.UsesMizzle || report.Issues.Count > 0)
            .ToList();
        var skipped = allReports.Count - reports.Count;

        AnsiConsole.MarkupLine($"[bold]Mizzle doctor:[/] {ConsoleText.Escape(target.DisplayName)}");
        AnsiConsole.MarkupLine(
            $"[grey]Checked {allReports.Count} {ProjectText(allReports.Count)}; showing {reports.Count}; skipped {skipped} non-Mizzle {ProjectText(skipped)}.[/]");
        AnsiConsole.WriteLine();
        if (reports.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No Mizzle projects found.[/]");
            return 0;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("Project");
        table.AddColumn("Dialect");
        table.AddColumn("Generators");
        table.AddColumn("Strict");
        table.AddColumn("Nullable");
        table.AddColumn("Issues");
        foreach (var report in reports)
        {
            table.AddRow(
                ConsoleText.Escape(report.ProjectName),
                Result(report.Dialect),
                Result(report.Generators),
                Result(report.StrictMode),
                Result(report.Nullable),
                report.Issues.Count == 0 ? "[green]0[/]" : $"[yellow]{report.Issues.Count}[/]");
        }

        AnsiConsole.Write(table);

        var issues = reports.SelectMany(r => r.Issues.Select(i => (Report: r, Issue: i))).ToList();
        if (issues.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Warnings[/]");
            foreach (var (report, issue) in issues)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]{ConsoleText.Escape(issue.Code)}[/] {ConsoleText.Escape(report.ProjectName)}: {ConsoleText.Escape(issue.Message)}");
            }
        }

        return issues.Count == 0 ? 0 : 1;
    }

    private static string Result(DoctorCheck check)
        => check.State switch
        {
            DoctorCheckState.Ok => $"[green]{ConsoleText.Escape(check.Text)}[/]",
            DoctorCheckState.Warn => $"[yellow]{ConsoleText.Escape(check.Text)}[/]",
            _ => $"[grey]{ConsoleText.Escape(check.Text)}[/]"
        };

    private static string ProjectText(int count)
        => count == 1 ? "project" : "projects";
}
