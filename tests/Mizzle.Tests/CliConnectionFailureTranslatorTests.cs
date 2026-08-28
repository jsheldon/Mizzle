using Mizzle.Cli.Infrastructure;

namespace Mizzle.Tests;

public sealed class CliConnectionFailureTranslatorTests
{
    [Fact]
    public void Translates_sql_server_untrusted_certificate_message()
    {
        var exception = new InvalidOperationException(
            "A connection was successfully established with the server, but then an error occurred during the login process. "
            + "(provider: SSL Provider, error: 0 - The certificate chain was issued by an authority that is not trusted.)");

        var failure = ConnectionFailureTranslator.TryTranslate(exception);

        Assert.NotNull(failure);
        Assert.Equal("MZCLI070", failure!.Code);
        Assert.Contains("TLS certificate", failure.Message, StringComparison.Ordinal);
        Assert.Contains("TrustServerCertificate=True", failure.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void Translates_nested_login_failure()
    {
        var exception = new InvalidOperationException(
            "outer",
            new InvalidOperationException("Login failed for user 'sa'."));

        var failure = ConnectionFailureTranslator.TryTranslate(exception);

        Assert.NotNull(failure);
        Assert.Equal("MZCLI071", failure!.Code);
    }

    [Fact]
    public void Translates_unreachable_sql_server_message()
    {
        var exception = new InvalidOperationException(
            "A network-related or instance-specific error occurred while establishing a connection to SQL Server. "
            + "The server was not found or was not accessible. "
            + "(provider: Named Pipes Provider, error: 40 - Could not open a connection to SQL Server)");

        var failure = ConnectionFailureTranslator.TryTranslate(exception);

        Assert.NotNull(failure);
        Assert.Equal("MZCLI073", failure!.Code);
        Assert.Contains("could not be reached", failure.Message, StringComparison.Ordinal);
    }
}
