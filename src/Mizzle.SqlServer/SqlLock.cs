using Mizzle.Ir;

namespace Mizzle.SqlServer;

internal static class SqlLock
{
    public static Task AcquireAsync(IQueryExecutor executor, string resource, CancellationToken cancellationToken)
    {
        var bag = new ParamBag();
        bag.Add(resource, typeof(string));
        return executor.ExecuteAsync(new LockQuery(resource), bag, overlay: null, cancellationToken);
    }
}
