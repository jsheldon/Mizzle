namespace Mizzle;

public sealed class UnsupportedFeatureException : Exception
{
    public Feature Feature { get; }
    public DialectKind Dialect { get; }
    public IReadOnlyList<DialectKind> SupportedBy { get; }

    public UnsupportedFeatureException(
        Feature feature,
        DialectKind dialect,
        IReadOnlyList<DialectKind> supportedBy)
        : base($"{feature} is not supported on {dialect}. Supported by: {string.Join(", ", supportedBy)}.")
    {
        Feature = feature;
        Dialect = dialect;
        SupportedBy = supportedBy;
    }
}
