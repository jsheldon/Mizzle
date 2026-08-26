namespace Mizzle.Postgres;

using Mizzle.Compile;

public sealed class PgCapabilities : IDialectCapabilities
{
    public static readonly PgCapabilities Instance = new();

    private static readonly HashSet<Feature> Supported =
    [
        Feature.RecursiveCte,
        Feature.DmlWithCte,
        Feature.Returning,
        Feature.Limit,
        Feature.Savepoint,
        Feature.AdvisoryLock,
        Feature.ILike,
        Feature.WindowCount
    ];

    public DialectKind Dialect => DialectKind.Postgres;

    public IReadOnlySet<Feature> All => Supported;

    public bool Supports(Feature feature) => Supported.Contains(feature);
}
