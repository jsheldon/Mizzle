using Mizzle.Cli.Doctor;

namespace Mizzle.Tests;

public sealed class CliProjectDoctorAnalyzerTests
{
    [Fact]
    public void Reports_mizzle_project_health_and_source_issues()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-doctor-analysis-" + Guid.NewGuid()));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <MizzleQueryMode>StrictMode</MizzleQueryMode>
                    <Nullable>disable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Mizzle.Postgres" Version="0.1.0-alpha.6" />
                    <PackageReference Include="Mizzle.Generators" Version="0.1.0-alpha.5" />
                  </ItemGroup>
                </Project>
                """);

            var app = root.CreateSubdirectory("App");
            var project = Path.Combine(app.FullName, "App.csproj");
            File.WriteAllText(project, "<Project />");
            File.WriteAllText(Path.Combine(app.FullName, "Users.cs"), """
                using Mizzle.Postgres;

                public sealed class Users : PgTable<Users>
                {
                    private const string EmailName = "email";
                    public Users() : base("users", "public", "u") { }
                    public PgColumn<string> Email { get; } = Text(EmailName);
                    public PgColumn<System.Guid> PublicId { get; } = Text("public_id").Map(s => System.Guid.Parse(s), g => g.ToString());
                }
                """);

            var report = ProjectDoctorAnalyzer.Analyze(ProjectDoctorReader.Read(project, root.FullName));

            Assert.True(report.UsesMizzle);
            Assert.Equal("postgres", report.Dialect.Text);
            Assert.Contains(report.Issues, i => i.Code == "MZCLI083");
            Assert.Contains(report.Issues, i => i.Code == "MZCLI085");
            Assert.Contains(report.Issues, i => i.Code == "MZCLI086");
            Assert.Contains(report.Issues, i => i.Code == "MZCLI087");
            Assert.Contains(report.Issues, i => i.Code == "MZCLI088");
            Assert.Contains(report.Issues, i => i.Code == "MZCLI089");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Reports_mizzle_looking_source_without_dialect_reference()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-doctor-source-" + Guid.NewGuid()));
        try
        {
            var project = Path.Combine(root.FullName, "App.csproj");
            File.WriteAllText(project, """
                <Project>
                  <PropertyGroup>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root.FullName, "Query.cs"), "using Mizzle.Fluent; class Q { }");

            var report = ProjectDoctorAnalyzer.Analyze(ProjectDoctorReader.Read(project, root.FullName));

            Assert.True(report.UsesMizzle);
            Assert.Contains(report.Issues, i => i.Code == "MZCLI081");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Does_not_treat_cli_namespace_usings_as_mizzle_consumer_source()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-doctor-cli-source-" + Guid.NewGuid()));
        try
        {
            var project = Path.Combine(root.FullName, "Tool.csproj");
            File.WriteAllText(project, """
                <Project>
                  <PropertyGroup>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root.FullName, "Command.cs"), "using Mizzle.Cli.Infrastructure; class Command { }");

            var report = ProjectDoctorAnalyzer.Analyze(ProjectDoctorReader.Read(project, root.FullName));

            Assert.False(report.UsesMizzle);
            Assert.DoesNotContain(report.Issues, i => i.Code == "MZCLI081");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Reports_non_literal_names_on_expanded_type_factories()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-doctor-expanded-factories-" + Guid.NewGuid()));
        try
        {
            var project = Path.Combine(root.FullName, "App.csproj");
            File.WriteAllText(project, """
                <Project>
                  <PropertyGroup>
                    <MizzleQueryMode>Strict</MizzleQueryMode>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root.FullName, "Readings.cs"), """
                using Mizzle.Postgres;

                public sealed class Readings : PgTable<Readings>
                {
                    private const string BalanceName = "balance";
                    public Readings() : base("readings", "public", "r") { }
                    public PgColumn<decimal> Balance { get; } = Money(BalanceName);
                }
                """);

            var report = ProjectDoctorAnalyzer.Analyze(ProjectDoctorReader.Read(project, root.FullName));

            Assert.Contains(report.Issues, i => i.Code == "MZCLI087");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Reports_mismatched_central_package_versions()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-doctor-central-versions-" + Guid.NewGuid()));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Directory.Packages.props"), """
                <Project>
                  <ItemGroup>
                    <PackageVersion Include="Mizzle.Postgres" Version="0.1.0-alpha.6" />
                    <PackageVersion Include="Mizzle.Generators" Version="0.1.0-alpha.5" />
                  </ItemGroup>
                </Project>
                """);
            var project = Path.Combine(root.FullName, "App.csproj");
            File.WriteAllText(project, """
                <Project>
                  <PropertyGroup>
                    <MizzleQueryMode>Strict</MizzleQueryMode>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Mizzle.Postgres" />
                    <PackageReference Include="Mizzle.Generators" />
                  </ItemGroup>
                </Project>
                """);

            var report = ProjectDoctorAnalyzer.Analyze(ProjectDoctorReader.Read(project, root.FullName));

            Assert.Contains(report.Issues, i => i.Code == "MZCLI089");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
