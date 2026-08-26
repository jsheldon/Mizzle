namespace Mizzle.SqlServer;

using Mizzle.Compile;

public sealed class SqlServerCapabilities : IDialectCapabilities
{
    public static readonly SqlServerCapabilities Instance = new();

    private static readonly HashSet<Feature> Supported =
    [
        Feature.RecursiveCte,
        Feature.DmlWithCte,
        Feature.Output,
        Feature.Fetch,
        Feature.Savepoint,
        Feature.AppLock,
        Feature.WindowCount
    ];

    public DialectKind Dialect => DialectKind.SqlServer;

    public IReadOnlySet<Feature> All => Supported;

    public bool Supports(Feature feature) => Supported.Contains(feature);
}
