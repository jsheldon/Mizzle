namespace Mizzle;

public static class InterceptorScope
{
    private static readonly AsyncLocal<int> Depth = new();

    public static bool Entered => Depth.Value > 0;

    public static IDisposable Enter()
    {
        Depth.Value += 1;
        return new Releaser();
    }

    private sealed class Releaser : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Depth.Value -= 1;
        }
    }
}
