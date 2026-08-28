using Mizzle.Cli.Infrastructure;

namespace Mizzle.Cli.Schema;

internal static class DatabaseInspector
{
    public static IDatabaseInspector For(ProviderKind provider)
        => provider switch
        {
            ProviderKind.Postgres => new PostgresInspector(),
            ProviderKind.SqlServer => new SqlServerInspector(),
            _ => throw new CliFailure("MZCLI002", $"Provider '{provider}' is not implemented.")
        };
}
