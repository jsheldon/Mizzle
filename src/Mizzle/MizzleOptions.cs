namespace Mizzle;

/// <summary>Connection-wide defaults, set when registering Mizzle in the container.</summary>
public sealed class MizzleOptions
{
    /// <summary>How long a command may run before the provider cancels it. Defaults to 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>
    ///     Throws when a query falls back to runtime compilation instead of running a
    ///     baked interceptor. Useful in tests to catch chains that stopped being
    ///     statically visible.
    /// </summary>
    public bool AssertCompiledQueries { get; set; }
}
