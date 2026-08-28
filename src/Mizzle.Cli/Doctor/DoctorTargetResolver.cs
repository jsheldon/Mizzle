using Mizzle.Cli.Infrastructure;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Mizzle.Cli.Doctor;

internal sealed record DoctorTarget(string DisplayName, string Root, IReadOnlyList<string> ProjectPaths);

internal static class DoctorTargetResolver
{
    public static DoctorTarget Resolve(string? project, string? solution, string currentDirectory)
    {
        var root = Path.GetFullPath(currentDirectory);
        if (!string.IsNullOrWhiteSpace(project) && !string.IsNullOrWhiteSpace(solution))
        {
            throw new CliFailure("MZCLI037", "Pass --project or --solution, not both.");
        }

        if (!string.IsNullOrWhiteSpace(project))
        {
            var fullProject = FullPathInsideRoot(project, root, "Project");
            if (!File.Exists(fullProject))
            {
                throw new CliFailure("MZCLI030", "Project file not found.", "Pass --project ./YourApp.csproj.");
            }

            return new DoctorTarget(Path.GetFileName(fullProject), root, [fullProject]);
        }

        if (!string.IsNullOrWhiteSpace(solution))
        {
            var fullSolution = FullPathInsideRoot(solution, root, "Solution");
            if (!File.Exists(fullSolution))
            {
                throw new CliFailure("MZCLI038", "Solution file not found.", "Pass --solution ./YourApp.slnx.");
            }

            return new DoctorTarget(Path.GetFileName(fullSolution), root, ValidateProjects(ReadSolutionProjects(fullSolution), root));
        }

        var solutions = Directory.EnumerateFiles(root, "*.sln")
            .Concat(Directory.EnumerateFiles(root, "*.slnx"))
            .ToList();
        if (solutions.Count == 1)
        {
            return new DoctorTarget(Path.GetFileName(solutions[0]), root, ValidateProjects(ReadSolutionProjects(solutions[0]), root));
        }

        if (solutions.Count > 1)
        {
            throw new CliFailure("MZCLI039", "More than one solution found in the current directory.", "Pass --solution <path>.");
        }

        var projects = Directory.EnumerateFiles(root, "*.csproj").ToList();
        if (projects.Count == 1)
        {
            return new DoctorTarget(Path.GetFileName(projects[0]), root, [projects[0]]);
        }

        if (projects.Count > 1)
        {
            throw new CliFailure("MZCLI041", "More than one project found in the current directory.", "Pass --project <path> or run from a project directory.");
        }

        throw new CliFailure("MZCLI042", "No project or solution found.", "Run from a directory containing one .csproj, .sln, or .slnx, or pass --project/--solution.");
    }

    private static IReadOnlyList<string> ReadSolutionProjects(string solution)
    {
        var directory = Path.GetDirectoryName(solution)!;
        return Path.GetExtension(solution).Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ReadSlnxProjects(solution, directory)
            : ReadSlnProjects(solution, directory);
    }

    private static IReadOnlyList<string> ValidateProjects(IReadOnlyList<string> projects, string root)
    {
        if (projects.Count == 0)
        {
            throw new CliFailure("MZCLI044", "No C# projects were found in the solution.");
        }

        foreach (var project in projects)
        {
            FullPathInsideRoot(project, root, "Project");
        }

        return projects;
    }

    private static IReadOnlyList<string> ReadSlnxProjects(string solution, string directory)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(solution);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            throw new CliFailure("MZCLI043", $"Could not read solution '{solution}'.", e.Message);
        }

        return [.. document.Descendants()
            .Where(e => e.Name.LocalName == "Project")
            .Select(e => e.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path) && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFullPath(Path.Combine(directory, path!)))
            .Where(File.Exists)];
    }

    private static IReadOnlyList<string> ReadSlnProjects(string solution, string directory)
    {
        var text = File.ReadAllText(solution);
        return [.. Regex.Matches(text, "Project\\(\"[^\"]+\"\\)\\s*=\\s*\"[^\"]+\",\\s*\"(?<path>[^\"]+\\.csproj)\"")
            .Select(m => Path.GetFullPath(Path.Combine(directory, m.Groups["path"].Value)))
            .Where(File.Exists)];
    }

    private static string FullPathInsideRoot(string path, string root, string label)
    {
        var full = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(root, full);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new CliFailure(
                "MZCLI036",
                $"{label} '{full}' is outside the current working directory.",
                $"Run mizzle doctor from '{Path.GetDirectoryName(full)}' or a parent directory you want it to inspect.");
        }

        return full;
    }
}
