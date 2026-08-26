namespace Mizzle.Tests;

file sealed class FakeCaps : IDialectCapabilities
{
    public required DialectKind Dialect { get; init; }
    public required HashSet<Feature> All { get; init; }
    IReadOnlySet<Feature> IDialectCapabilities.All => All;
    public bool Supports(Feature feature) => All.Contains(feature);
}

public sealed class CapabilityCheckerTests
{
    [Fact]
    public void Check_passes_when_all_features_are_supported()
    {
        var caps = new FakeCaps
        {
            Dialect = DialectKind.Postgres,
            All = [Feature.ILike, Feature.Returning]
        };
        CapabilityChecker.Check([Feature.ILike], caps);
    }

    [Fact]
    public void Check_throws_for_ilike_on_sql_server()
    {
        var caps = new FakeCaps
        {
            Dialect = DialectKind.SqlServer,
            All = [Feature.Output, Feature.Fetch]
        };
        var ex = Assert.Throws<UnsupportedFeatureException>(
            () => CapabilityChecker.Check([Feature.ILike], caps));
        Assert.Equal(Feature.ILike, ex.Feature);
        Assert.Equal(DialectKind.SqlServer, ex.Dialect);
        Assert.Equal([DialectKind.Postgres], ex.SupportedBy);
    }

    [Fact]
    public void WhoSupports_matches_phase1_matrix()
    {
        Assert.Equal([DialectKind.Postgres], FeatureSupport.WhoSupports(Feature.ILike));
        Assert.Equal([DialectKind.SqlServer], FeatureSupport.WhoSupports(Feature.Output));
        Assert.Equal([DialectKind.Postgres, DialectKind.SqlServer], FeatureSupport.WhoSupports(Feature.RecursiveCte));
    }
}
