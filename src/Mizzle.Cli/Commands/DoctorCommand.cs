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
        public string Project { get; init; } = "";
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Project) || !File.Exists(settings.Project))
        {
            throw new CliFailure("MZCLI030", "Project file not found.", "Pass --project ./YourApp.csproj.");
        }

        var info = ProjectDoctorReader.Read(settings.Project);
        var checks = new Table().RoundedBorder();
        checks.AddColumn("Check");
        checks.AddColumn("Result");
        checks.AddRow("Mizzle package/project reference", ReferenceResult(info, "Mizzle.Postgres", "Mizzle.SqlServer"));
        checks.AddRow("Generators package/project reference", ReferenceResult(info, "Mizzle.Generators"));
        checks.AddRow("Strict mode", PropertyResult(info, "MizzleQueryMode", "Strict"));
        checks.AddRow("Nullable", PropertyResult(info, "Nullable", "enable"));
        AnsiConsole.Write(checks);
        return 0;
    }

    private static string ReferenceResult(ProjectDoctorInfo info, params string[] names)
    {
        var package = FindItem(info, "PackageReference", names);
        var project = FindItem(info, "ProjectReference", names);
        var analyzer = FindItem(info, "Analyzer", names);
        var item = package ?? project ?? analyzer;
        if (item is null)
        {
            return "[yellow]missing[/]";
        }

        var source = Path.GetFileName(item.Source);
        var kind = analyzer is not null && item == analyzer ? "analyzer" : package is not null && item == package ? "package" : "project";
        return $"[green]found[/] ({kind}, {source})";
    }

    private static ProjectItem? FindItem(ProjectDoctorInfo info, string itemName, params string[] names)
    {
        return info.Items.FirstOrDefault(item =>
            string.Equals(item.Name, itemName, StringComparison.OrdinalIgnoreCase)
            && 
            names.Any(name =>
                string.Equals(item.Identity, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileNameWithoutExtension(item.Identity), name, StringComparison.OrdinalIgnoreCase)
                || item.Identity.Contains(name, StringComparison.OrdinalIgnoreCase)));
    }

    private static string PropertyResult(ProjectDoctorInfo info, string property, string expected)
    {
        if (!info.Properties.TryGetValue(property, out var propertyValue) || string.IsNullOrWhiteSpace(propertyValue.Value))
        {
            return "[grey]not enabled[/]";
        }

        return string.Equals(propertyValue.Value, expected, StringComparison.OrdinalIgnoreCase)
            ? $"[green]enabled[/] ({propertyValue.Value}, {Path.GetFileName(propertyValue.Source)})"
            : $"[yellow]{propertyValue.Value}[/] ({Path.GetFileName(propertyValue.Source)})";
    }
}
