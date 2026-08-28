namespace Mizzle.Cli.Infrastructure;

internal static class ConnectionFailureTranslator
{
    public static CliFailure? TryTranslate(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (IsSqlServerUntrustedCertificate(current.Message))
            {
                return new CliFailure(
                    "MZCLI070",
                    "SQL Server rejected the TLS certificate during login.",
                    "For local/dev databases, add 'TrustServerCertificate=True' to the connection string. For shared environments, install or configure a trusted certificate instead.");
            }

            if (current.Message.Contains("password authentication failed", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("login failed for user", StringComparison.OrdinalIgnoreCase))
            {
                return new CliFailure(
                    "MZCLI071",
                    "Database login failed.",
                    "Check the username, password, database name, and authentication mode in the connection string.");
            }

            if (current.Message.Contains("No such host is known", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase))
            {
                return new CliFailure(
                    "MZCLI072",
                    "Database host could not be resolved.",
                    "Check the server or host name in the connection string.");
            }

            if (current.Message.Contains("server was not found or was not accessible", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("Could not open a connection to SQL Server", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("connection refused", StringComparison.OrdinalIgnoreCase))
            {
                return new CliFailure(
                    "MZCLI073",
                    "Database server could not be reached.",
                    "Check the host, port, instance name, firewall, container state, and whether the database allows remote connections.");
            }
        }

        return null;
    }

    private static bool IsSqlServerUntrustedCertificate(string message)
        => message.Contains("certificate chain was issued by an authority that is not trusted", StringComparison.OrdinalIgnoreCase)
            || (message.Contains("SSL Provider", StringComparison.OrdinalIgnoreCase)
                && message.Contains("certificate", StringComparison.OrdinalIgnoreCase)
                && message.Contains("not trusted", StringComparison.OrdinalIgnoreCase));
}
