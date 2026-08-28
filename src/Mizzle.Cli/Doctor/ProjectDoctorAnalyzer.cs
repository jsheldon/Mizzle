using System.Text.RegularExpressions;

namespace Mizzle.Cli.Doctor;

internal enum DoctorCheckState
{
    Info,
    Ok,
    Warn,
}

internal sealed record DoctorCheck(DoctorCheckState State, string Text);

internal sealed record DoctorIssue(string Code, string Message);

internal sealed record ProjectDoctorReport(
    string ProjectName,
    bool UsesMizzle,
    DoctorCheck Dialect,
    DoctorCheck Generators,
    DoctorCheck StrictMode,
    DoctorCheck Nullable,
    IReadOnlyList<DoctorIssue> Issues);

internal static class ProjectDoctorAnalyzer
{
    public static ProjectDoctorReport Analyze(ProjectDoctorInfo info)
    {
        var project = Path.GetFileNameWithoutExtension(info.ProjectPath);
        var issues = new List<DoctorIssue>();
        var postgres = FindItem(info, "Mizzle.Postgres");
        var sqlServer = FindItem(info, "Mizzle.SqlServer");
        var generator = FindItem(info, "Mizzle.Generators");
        var source = ReadProjectSource(info);
        var code = StripStringsAndComments(source);
        var internalProject = IsMizzleInternalProject(project);
        var diagnosticProject = IsTestOrBenchmarkProject(project);
        var sourceLooksMizzle = !internalProject && LooksLikeMizzleSource(code);
        var usesMizzle = !internalProject && (postgres is not null || sqlServer is not null || generator is not null || sourceLooksMizzle);
        var warnOnConfiguration = usesMizzle && !diagnosticProject;

        if (sourceLooksMizzle && postgres is null && sqlServer is null)
        {
            issues.Add(new DoctorIssue("MZCLI081", "Mizzle-looking source found but no dialect package/project reference was found."));
        }

        if (warnOnConfiguration && postgres is not null && sqlServer is not null)
        {
            issues.Add(new DoctorIssue("MZCLI080", "Both Mizzle.Postgres and Mizzle.SqlServer are referenced."));
        }

        var strict = Property(info, "MizzleQueryMode");
        if (warnOnConfiguration && strict is null)
        {
            issues.Add(new DoctorIssue("MZCLI082", "MizzleQueryMode is not set to Strict."));
        }
        else if (warnOnConfiguration && strict is not null && !strict.Value.Equals("Strict", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new DoctorIssue("MZCLI083", $"MizzleQueryMode is '{strict.Value}', not 'Strict'."));
        }

        var nullable = Property(info, "Nullable");
        if (warnOnConfiguration && nullable is null)
        {
            issues.Add(new DoctorIssue("MZCLI084", "Nullable is not enabled."));
        }
        else if (warnOnConfiguration && nullable is not null && !nullable.Value.Equals("enable", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new DoctorIssue("MZCLI085", $"Nullable is '{nullable.Value}', not 'enable'."));
        }

        if (warnOnConfiguration && Regex.IsMatch(source, @"base\s*\(\s*""[^""]+""\s*,\s*""[^""]+""\s*,", RegexOptions.Multiline))
        {
            issues.Add(new DoctorIssue("MZCLI086", "Old table constructor alias syntax was found. Use WithAlias at the query site."));
        }

        if (warnOnConfiguration && Regex.IsMatch(code, @"\b(?:Text|NText|Varchar|Char|Integer|Int|SmallInt|TinyInt|BigInt|Decimal|Numeric|Real|Float|Boolean|Bit|Uuid|UniqueIdentifier|Date|Timestamptz|DateTime|DateTime2|Timestamp|Identity|NVarChar|NVarCharMax|VarChar)\s*\(\s*@?[A-Za-z_]", RegexOptions.Multiline))
        {
            issues.Add(new DoctorIssue("MZCLI087", "A column factory appears to use a non-literal database name."));
        }

        if (warnOnConfiguration && Regex.IsMatch(code, @"\.Map\s*\(\s*(?:\([^)]*\)\s*=>|[A-Za-z_][\w]*\s*=>)", RegexOptions.Multiline))
        {
            issues.Add(new DoctorIssue("MZCLI088", "A column Map call appears to use a lambda. Use static method references."));
        }

        var versionGroups = info.Items
            .Where(i => (i.Name is "PackageReference" or "PackageVersion")
                && i.Identity.StartsWith("Mizzle.", StringComparison.OrdinalIgnoreCase)
                && i.Version is { Length: > 0 })
            .GroupBy(i => i.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (warnOnConfiguration && versionGroups.Count > 1)
        {
            issues.Add(new DoctorIssue("MZCLI089", "Mizzle package references use more than one version."));
        }

        var dialect = (postgres, sqlServer) switch
        {
            (not null, null) => Ok("postgres"),
            (null, not null) => Ok("sqlserver"),
            (not null, not null) => Warn("mixed"),
            _ => usesMizzle ? Warn("missing") : Info("none")
        };

        return new ProjectDoctorReport(
            project,
            usesMizzle,
            diagnosticProject ? Info(dialect.Text) : dialect,
            diagnosticProject ? Info("not checked") : generator is null ? (usesMizzle ? Warn("not explicit") : Info("none")) : Ok(SourceText(generator)),
            diagnosticProject ? Info("not checked") : strict is null ? Info("not enabled") : strict.Value.Equals("Strict", StringComparison.OrdinalIgnoreCase) ? Ok("enabled") : Warn(strict.Value),
            diagnosticProject ? Info("not checked") : nullable is null ? Warn("not enabled") : nullable.Value.Equals("enable", StringComparison.OrdinalIgnoreCase) ? Ok("enabled") : Warn(nullable.Value),
            issues);
    }

    private static ProjectItem? FindItem(ProjectDoctorInfo info, string identity)
        => info.Items.FirstOrDefault(item =>
            item.Identity.Contains(identity, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileNameWithoutExtension(item.Identity), identity, StringComparison.OrdinalIgnoreCase));

    private static ProjectProperty? Property(ProjectDoctorInfo info, string name)
        => info.Properties.TryGetValue(name, out var value) ? value : null;

    private static string ReadProjectSource(ProjectDoctorInfo info)
    {
        var projectDirectory = Path.GetDirectoryName(info.ProjectPath);
        if (projectDirectory is null)
        {
            return "";
        }

        return string.Join(
            "\n",
            Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
    }

    private static bool LooksLikeMizzleSource(string text)
        => Regex.IsMatch(text, @"\busing\s+Mizzle(?:\.Fluent|\.Postgres|\.SqlServer|\.Schema)?\s*;", RegexOptions.Multiline)
            || Regex.IsMatch(text, @"\b(?:PgTable|SqlTable|PgColumn|SqlColumn)\s*<", RegexOptions.Multiline)
            || Regex.IsMatch(text, @"\.(?:ToListAsync|FirstAsync|SingleAsync|ToPageAsync|ToCursorPageAsync)\s*<", RegexOptions.Multiline);

    private static bool IsMizzleInternalProject(string project)
        => project is "Mizzle" or "Mizzle.Postgres" or "Mizzle.SqlServer" or "Mizzle.Generators" or "Mizzle.Cli";

    private static bool IsTestOrBenchmarkProject(string project)
        => project.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            || project.EndsWith(".Benchmarks", StringComparison.OrdinalIgnoreCase);

    private static string StripStringsAndComments(string source)
    {
        var text = Regex.Replace(source, "\"\"\".*?\"\"\"", "", RegexOptions.Singleline);
        text = Regex.Replace(text, "@\"(?:\"\"|[^\"])*\"", "\"\"", RegexOptions.Singleline);
        text = Regex.Replace(text, "\\$?\"(?:\\\\.|[^\"\\\\])*\"", "\"\"", RegexOptions.Singleline);
        text = Regex.Replace(text, "/\\*.*?\\*/", "", RegexOptions.Singleline);
        return Regex.Replace(text, "//.*?$", "", RegexOptions.Multiline);
    }

    private static DoctorCheck Ok(string text) => new(DoctorCheckState.Ok, text);

    private static DoctorCheck Warn(string text) => new(DoctorCheckState.Warn, text);

    private static DoctorCheck Info(string text) => new(DoctorCheckState.Info, text);

    private static string SourceText(ProjectItem item) => item.Name.ToLowerInvariant();
}
