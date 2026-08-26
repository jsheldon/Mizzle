namespace Mizzle.Compile;

public interface IDialectCapabilities
{
    DialectKind Dialect { get; }
    bool Supports(Feature feature);
    IReadOnlySet<Feature> All { get; }
}
