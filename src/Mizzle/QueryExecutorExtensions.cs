using Mizzle.Ir;

namespace Mizzle;

public static class QueryExecutorExtensions
{
    public static Task<int> ExecuteAsync(
        this IQueryExecutor executor,
        Query query,
        CancellationToken cancellationToken = default)
        => executor.ExecuteAsync(query, overlay: null, cancellationToken);
}
