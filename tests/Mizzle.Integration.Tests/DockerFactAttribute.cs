namespace Mizzle.Integration.Tests;

/// <summary>
///     A Fact that skips instead of failing when there is no Docker daemon to run
///     Testcontainers against. CI has Docker, so these still run there; a
///     contributor without it gets skips rather than a wall of red.
/// </summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerProbe.IsAvailable)
        {
            Skip = "Docker is not available; integration tests need a running daemon.";
        }
    }
}

internal static class DockerProbe
{
    private static readonly Lazy<bool> Available = new(Probe, isThreadSafe: true);

    public static bool IsAvailable => Available.Value;

    private static bool Probe()
    {
        // An explicit endpoint means someone configured Docker deliberately.
        foreach (var variable in new[] { "DOCKER_HOST", "TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE" })
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
            {
                return true;
            }
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                return Directory.GetFiles(@"\.\pipe\")
                    .Any(pipe => pipe.EndsWith("docker_engine", StringComparison.OrdinalIgnoreCase));
            }

            return File.Exists("/var/run/docker.sock");
        }
        catch (Exception)
        {
            // A probe that cannot answer should not decide the tests are unrunnable.
            return true;
        }
    }
}
