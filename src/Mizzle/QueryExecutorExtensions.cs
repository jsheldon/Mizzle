using Mizzle.Ir;

namespace Mizzle;

public static class QueryExecutorExtensions
{
    public static Task<int> ExecuteAsync(
        this IQueryExecutor executor,
        Query query,
        ParamBag bag,
        CancellationToken cancellationToken = default)
        => executor.ExecuteAsync(query, bag, overlay: null, cancellationToken);
}
