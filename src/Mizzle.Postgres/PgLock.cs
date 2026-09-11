using Mizzle.Ir;

namespace Mizzle.Postgres;

internal static class PgLock
{
    // A non-null timeout is SQL-Server-only (pg_advisory_xact_lock always waits indefinitely,
    // with no server-side bounded-wait equivalent) -- passing one here still throws
    // UnsupportedFeatureException, via the same Feature/CapabilityChecker gate the emitter
    // already runs, rather than silently ignoring it.
    public static Task AcquireAsync(
        IQueryExecutor executor, string resource, TimeSpan? timeout, CancellationToken cancellationToken)
        => executor.ExecuteAsync(new LockQuery(resource, timeout), overlay: null, cancellationToken);
}
