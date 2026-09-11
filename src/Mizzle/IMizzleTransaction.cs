namespace Mizzle;

public interface IMizzleTransaction : IQueryExecutor, IAsyncDisposable
{
    /// <summary>
    ///     Takes an exclusive application/advisory lock scoped to this transaction, released
    ///     automatically on commit or rollback. <paramref name="timeout"/> is SQL Server only
    ///     today (maps to <c>sp_getapplock</c>'s <c>@LockTimeout</c>); <c>null</c> waits
    ///     indefinitely. Passing a timeout on Postgres throws <see cref="UnsupportedFeatureException"/>.
    ///     On SQL Server, a negative result from <c>sp_getapplock</c> (timeout, deadlock victim,
    ///     cancelled, or another failure) throws <see cref="LockAcquisitionException"/> rather
    ///     than silently continuing without the lock.
    /// </summary>
    Task LockAsync(string resource, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
