using Mizzle.Cli.Infrastructure;
using System.Reflection;

namespace Mizzle.Tests;

public sealed class CliVersionInfoTests
{
    [Fact]
    public void Uses_assembly_informational_version()
    {
        var version = VersionInfo.FullVersion(typeof(VersionInfo).Assembly);

        Assert.Contains("0.1.0-alpha", version, StringComparison.Ordinal);
    }

    [Fact]
    public void Package_version_omits_build_metadata()
    {
        var version = VersionInfo.PackageVersion(typeof(VersionInfo).Assembly);

        Assert.Equal("0.1.0-alpha", version);
    }

    [Fact]
    public void Falls_back_to_assembly_version()
    {
        var version = VersionInfo.PackageVersion(typeof(string).Assembly);

        Assert.NotEqual("unknown", version);
    }
}
