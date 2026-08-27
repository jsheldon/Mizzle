using Mizzle.Ir;

namespace Mizzle.SqlServer;

internal static class SqlLock
{
    public static Task AcquireAsync(IQueryExecutor executor, string resource, CancellationToken cancellationToken)
        => executor.ExecuteAsync(new LockQuery(resource), overlay: null, cancellationToken);
}
