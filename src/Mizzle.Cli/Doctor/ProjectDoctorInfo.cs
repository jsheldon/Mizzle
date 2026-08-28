using Mizzle.Cli.Infrastructure;
using System.Xml.Linq;

namespace Mizzle.Cli.Doctor;

internal sealed record ProjectProperty(string Name, string Value, string Source);

internal sealed record ProjectItem(string Name, string Identity, string Source, string? Version);

internal sealed record ProjectDoctorInfo(
    string ProjectPath,
    IReadOnlyDictionary<string, ProjectProperty> Properties,
    IReadOnlyList<ProjectItem> Items,
    IReadOnlyList<string> FilesRead);

internal static class ProjectDoctorReader
{
    private static readonly string[] AncestorFiles =
    [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
    ];

    public static ProjectDoctorInfo Read(string project)
        => Read(project, Environment.CurrentDirectory);

    internal static ProjectDoctorInfo Read(string project, string currentDirectory)
    {
        var fullProject = Path.GetFullPath(project);
        var root = Path.GetFullPath(currentDirectory);
        if (!File.Exists(fullProject))
        {
            throw new CliFailure("MZCLI030", "Project file not found.", "Pass --project ./YourApp.csproj.");
        }

        if (!IsInDirectory(fullProject, root))
        {
            throw new CliFailure(
                "MZCLI036",
                $"Project '{fullProject}' is outside the current working directory.",
                $"Run mizzle doctor from '{Path.GetDirectoryName(fullProject)}' or a parent directory you want it to inspect.");
        }

        var files = DiscoverFiles(fullProject, root);
        var properties = new Dictionary<string, ProjectProperty>(StringComparer.OrdinalIgnoreCase);
        var items = new List<ProjectItem>();
        foreach (var file in files)
        {
            ReadFile(file, properties, items);
        }

        return new ProjectDoctorInfo(fullProject, properties, items, files);
    }

    private static IReadOnlyList<string> DiscoverFiles(string project, string root)
    {
        var projectDirectory = Path.GetDirectoryName(project)
            ?? throw new CliFailure("MZCLI034", $"Could not determine project directory for '{project}'.");
        var ancestors = new List<string>();
        for (var dir = new DirectoryInfo(projectDirectory); dir is not null; dir = dir.Parent)
        {
            ancestors.Add(dir.FullName);
            if (SamePath(dir.FullName, root))
            {
                break;
            }
        }

        ancestors.Reverse();
        var files = new List<string>();
        foreach (var dir in ancestors)
        {
            foreach (var name in AncestorFiles)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                {
                    files.Add(candidate);
                }
            }
        }

        files.Add(project);
        return files;
    }

    private static bool IsInDirectory(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return relative.Length == 0
            || (!relative.StartsWith("..", StringComparison.Ordinal)
                && !Path.IsPathRooted(relative));
    }

    private static bool SamePath(string left, string right)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void ReadFile(string file, Dictionary<string, ProjectProperty> properties, List<ProjectItem> items)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(file, LoadOptions.None);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            throw new CliFailure("MZCLI035", $"Could not read project metadata from '{file}'.", e.Message);
        }

        foreach (var element in document.Descendants().Where(e => e.Name.LocalName is "MizzleQueryMode" or "Nullable"))
        {
            var value = element.Value.Trim();
            if (value.Length > 0)
            {
                properties[element.Name.LocalName] = new ProjectProperty(element.Name.LocalName, value, file);
            }
        }

        foreach (var element in document.Descendants().Where(e => e.Name.LocalName is "PackageReference" or "PackageVersion" or "ProjectReference" or "Analyzer"))
        {
            var identity = element.Attribute("Include")?.Value
                ?? element.Attribute("Update")?.Value
                ?? element.Attribute("Remove")?.Value
                ?? "";
            if (identity.Length > 0)
            {
                var version = element.Attribute("Version")?.Value;
                items.Add(new ProjectItem(element.Name.LocalName, identity, file, version));
            }
        }
    }
}
