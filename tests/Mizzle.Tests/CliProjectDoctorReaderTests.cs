using Mizzle.Cli.Doctor;
using Mizzle.Cli.Infrastructure;

namespace Mizzle.Tests;

public sealed class CliProjectDoctorReaderTests
{
    [Fact]
    public void Reads_project_and_inherited_directory_build_props()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-doctor-" + Guid.NewGuid()));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "App.slnx"), "<Solution />");
            File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <MizzleQueryMode>Strict</MizzleQueryMode>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Mizzle.Postgres" Version="0.1.0-alpha.6" />
                  </ItemGroup>
                </Project>
                """);

            var src = root.CreateSubdirectory("src").CreateSubdirectory("App");
            var project = Path.Combine(src.FullName, "App.csproj");
            File.WriteAllText(project, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="..\..\Mizzle.Generators\Mizzle.Generators.csproj" />
                  </ItemGroup>
                </Project>
                """);

            var info = ProjectDoctorReader.Read(project, root.FullName);

            Assert.Equal("Strict", info.Properties["MizzleQueryMode"].Value);
            Assert.Equal("Directory.Build.props", Path.GetFileName(info.Properties["MizzleQueryMode"].Source));
            Assert.Equal("enable", info.Properties["Nullable"].Value);
            Assert.Contains(info.Items, i => i.Name == "PackageReference" && i.Identity == "Mizzle.Postgres");
            Assert.Contains(info.Items, i => i.Name == "ProjectReference" && i.Identity.Contains("Mizzle.Generators", StringComparison.Ordinal));
            Assert.Contains(info.FilesRead, f => Path.GetFileName(f) == "Directory.Build.props");
            Assert.Contains(info.FilesRead, f => Path.GetFileName(f) == "App.csproj");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Rejects_project_outside_current_working_directory()
    {
        var cwd = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-doctor-cwd-" + Guid.NewGuid()));
        var outside = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-doctor-outside-" + Guid.NewGuid()));
        try
        {
            var project = Path.Combine(outside.FullName, "Outside.csproj");
            File.WriteAllText(project, "<Project />");

            var ex = Assert.Throws<CliFailure>(() => ProjectDoctorReader.Read(project, cwd.FullName));

            Assert.Equal("MZCLI036", ex.Code);
            Assert.Contains("outside the current working directory", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            cwd.Delete(recursive: true);
            outside.Delete(recursive: true);
        }
    }

    [Fact]
    public void Stops_at_current_working_directory()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-doctor-parent-" + Guid.NewGuid()));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <MizzleQueryMode>Strict</MizzleQueryMode>
                  </PropertyGroup>
                </Project>
                """);
            var cwd = root.CreateSubdirectory("repo");
            var app = cwd.CreateSubdirectory("src").CreateSubdirectory("App");
            var project = Path.Combine(app.FullName, "App.csproj");
            File.WriteAllText(project, """
                <Project>
                  <PropertyGroup>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);

            var info = ProjectDoctorReader.Read(project, cwd.FullName);

            Assert.False(info.Properties.ContainsKey("MizzleQueryMode"));
            Assert.Equal("enable", info.Properties["Nullable"].Value);
            Assert.DoesNotContain(info.FilesRead, f => Path.GetDirectoryName(f) == root.FullName);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
