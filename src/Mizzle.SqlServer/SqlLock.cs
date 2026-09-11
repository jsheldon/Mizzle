using Mizzle.Ir;

namespace Mizzle.SqlServer;

internal static class SqlLock
{
    public static async Task AcquireAsync(
        IQueryExecutor executor,
        string resource,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var rows = await executor.QueryAsync(
            new LockQuery(resource, timeout), static r => r.GetInt32(0), overlay: null, cancellationToken);
        var result = rows.Count > 0 ? rows[0] : 0;
        if (result < 0)
        {
            throw new LockAcquisitionException(resource, result);
        }
    }
}
