using System.Data.Common;
using Mizzle.Ir;

namespace Mizzle;

public interface IQueryExecutor
{
    Task<IReadOnlyList<T>> QueryAsync<T>(
        Query query,
        ParamBag bag,
        Func<DbDataReader, T> map,
        QueryOptions? overlay,
        CancellationToken cancellationToken);

    Task<int> ExecuteAsync(
        Query query,
        ParamBag bag,
        QueryOptions? overlay,
        CancellationToken cancellationToken);

    IAsyncEnumerable<T> StreamAsync<T>(
        Query query,
        ParamBag bag,
        Func<DbDataReader, T> map,
        QueryOptions? overlay,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<T>> QueryPrecompiledAsync<T>(
        string sql,
        ParamBag bag,
        Func<DbDataReader, T> map,
        QueryOptions? overlay,
        CancellationToken cancellationToken);
}
