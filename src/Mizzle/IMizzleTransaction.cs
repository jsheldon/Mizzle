namespace Mizzle;

public interface IMizzleTransaction : IQueryExecutor, IAsyncDisposable
{
    Task LockAsync(string resource, CancellationToken cancellationToken = default);
}
