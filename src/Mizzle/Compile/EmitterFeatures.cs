namespace Mizzle.Compile;

public static class EmitterFeatures
{
    public static IReadOnlyList<Feature> For(DialectKind dialect, IReadOnlyList<Feature> collected)
    {
        if (dialect != DialectKind.SqlServer)
        {
            return collected;
        }

        return
        [
            ..collected.Select(feature => feature switch
            {
                Feature.Limit => Feature.Fetch,
                Feature.Returning => Feature.Output,
                Feature.AdvisoryLock => Feature.AppLock,
                _ => feature
            })
        ];
    }
}
