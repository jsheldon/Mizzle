namespace Mizzle.Compile;

public static class CapabilityChecker
{
    public static void Check(IEnumerable<Feature> used, IDialectCapabilities capabilities)
    {
        foreach (var feature in used)
        {
            if (!capabilities.Supports(feature))
            {
                throw new UnsupportedFeatureException(
                    feature,
                    capabilities.Dialect,
                    FeatureSupport.WhoSupports(feature));
            }
        }
    }
}
