namespace Mizzle;

public sealed class MizzleOptions
{
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool AssertCompiledQueries { get; set; }
}
