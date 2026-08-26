namespace Mizzle.Compile;

public static class FeatureSupport
{
    private static readonly IReadOnlyDictionary<Feature, DialectKind[]> Matrix =
        new Dictionary<Feature, DialectKind[]>
        {
            [Feature.RecursiveCte] = [DialectKind.Postgres, DialectKind.SqlServer],
            [Feature.DmlWithCte] = [DialectKind.Postgres, DialectKind.SqlServer],
            [Feature.Returning] = [DialectKind.Postgres],
            [Feature.Output] = [DialectKind.SqlServer],
            [Feature.Limit] = [DialectKind.Postgres],
            [Feature.Fetch] = [DialectKind.SqlServer],
            [Feature.Savepoint] = [DialectKind.Postgres, DialectKind.SqlServer],
            [Feature.AdvisoryLock] = [DialectKind.Postgres],
            [Feature.AppLock] = [DialectKind.SqlServer],
            [Feature.ILike] = [DialectKind.Postgres],
            [Feature.WindowCount] = [DialectKind.Postgres, DialectKind.SqlServer],
        };

    public static IReadOnlyList<DialectKind> WhoSupports(Feature feature)
        => Matrix[feature];
}
