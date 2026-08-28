using System.Reflection;

namespace Mizzle.Cli.Infrastructure;

internal static class VersionInfo
{
    public static string PackageVersion(Assembly assembly)
    {
        var version = FullVersion(assembly);
        var metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        return metadataIndex < 0 ? version : version[..metadataIndex];
    }

    public static string FullVersion(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
