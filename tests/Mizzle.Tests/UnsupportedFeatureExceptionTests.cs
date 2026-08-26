namespace Mizzle.Tests;

public sealed class UnsupportedFeatureExceptionTests
{
    [Fact]
    public void Message_names_feature_dialect_and_supporters()
    {
        var ex = new UnsupportedFeatureException(
            Feature.ILike,
            DialectKind.SqlServer,
            [DialectKind.Postgres]);

        Assert.Equal(Feature.ILike, ex.Feature);
        Assert.Equal(DialectKind.SqlServer, ex.Dialect);
        Assert.Equal([DialectKind.Postgres], ex.SupportedBy);
        Assert.Contains("ILike", ex.Message, StringComparison.Ordinal);
        Assert.Contains("SqlServer", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Postgres", ex.Message, StringComparison.Ordinal);
    }
}
