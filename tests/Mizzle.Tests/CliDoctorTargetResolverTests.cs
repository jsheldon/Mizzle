using Mizzle.Cli.Doctor;
using Mizzle.Cli.Infrastructure;

namespace Mizzle.Tests;

public sealed class CliDoctorTargetResolverTests
{
    [Fact]
    public void Resolves_single_project_in_current_directory()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-doctor-target-" + Guid.NewGuid()));
        try
        {
            var project = Path.Combine(root.FullName, "App.csproj");
            File.WriteAllText(project, "<Project />");

            var target = DoctorTargetResolver.Resolve(null, null, root.FullName);

            Assert.Equal("App.csproj", target.DisplayName);
            Assert.Equal([project], target.ProjectPaths);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Resolves_slnx_projects_in_current_directory()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-doctor-slnx-" + Guid.NewGuid()));
        try
        {
            var src = root.CreateSubdirectory("src");
            var app = src.CreateSubdirectory("App");
            var data = src.CreateSubdirectory("Data");
            var appProject = Path.Combine(app.FullName, "App.csproj");
            var dataProject = Path.Combine(data.FullName, "Data.csproj");
            File.WriteAllText(appProject, "<Project />");
            File.WriteAllText(dataProject, "<Project />");
            File.WriteAllText(Path.Combine(root.FullName, "Demo.slnx"), """
                <Solution>
                  <Project Path="src/App/App.csproj" />
                  <Project Path="src/Data/Data.csproj" />
                </Solution>
                """);

            var target = DoctorTargetResolver.Resolve(null, null, root.FullName);

            Assert.Equal("Demo.slnx", target.DisplayName);
            Assert.Equal([appProject, dataProject], target.ProjectPaths);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Multiple_projects_without_solution_requires_explicit_project()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-doctor-many-" + Guid.NewGuid()));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "A.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(root.FullName, "B.csproj"), "<Project />");

            var ex = Assert.Throws<CliFailure>(() => DoctorTargetResolver.Resolve(null, null, root.FullName));

            Assert.Equal("MZCLI041", ex.Code);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Solution_project_outside_current_directory_is_rejected()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-doctor-sln-root-" + Guid.NewGuid()));
        var sibling = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-doctor-sln-sibling-" + Guid.NewGuid()));
        try
        {
            var project = Path.Combine(sibling.FullName, "App.csproj");
            File.WriteAllText(project, "<Project />");
            File.WriteAllText(Path.Combine(root.FullName, "Demo.slnx"), $"""
                <Solution>
                  <Project Path="../{Path.GetFileName(sibling.FullName)}/App.csproj" />
                </Solution>
                """);

            var ex = Assert.Throws<CliFailure>(() => DoctorTargetResolver.Resolve(null, null, root.FullName));

            Assert.Equal("MZCLI036", ex.Code);
        }
        finally
        {
            root.Delete(recursive: true);
            sibling.Delete(recursive: true);
        }
    }
}
