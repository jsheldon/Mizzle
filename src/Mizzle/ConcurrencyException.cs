namespace Mizzle;

/// <summary>
///     Thrown when a statement affected a different number of rows than
///     <c>Expect(...)</c> required -- typically because a concurrent write moved the
///     row, or a version column no longer matches.
/// </summary>
public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException(int expected, int actual)
        : base($"Expected {expected} affected row(s) but {actual} were affected.")
    {
        Expected = expected;
        Actual = actual;
    }

    /// <summary>The row count the statement required.</summary>
    public int Expected { get; }

    /// <summary>The row count the database reported.</summary>
    public int Actual { get; }
}
