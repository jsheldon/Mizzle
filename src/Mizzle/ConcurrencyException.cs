namespace Mizzle;

public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException(int expected, int actual)
        : base($"Expected {expected} affected row(s) but {actual} were affected.")
    {
        Expected = expected;
        Actual = actual;
    }

    public int Expected { get; }

    public int Actual { get; }
}
