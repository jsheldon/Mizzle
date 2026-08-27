using Mizzle.Ir;

namespace Mizzle.Postgres;

internal static class PgLock
{
    public static Task AcquireAsync(IQueryExecutor executor, string resource, CancellationToken cancellationToken)
        => executor.ExecuteAsync(new LockQuery(resource), overlay: null, cancellationToken);
}
