using System.Data.Common;
using Mizzle.Ir;

namespace Mizzle;

public interface IQueryExecutor
{
    Task<IReadOnlyList<T>> QueryAsync<T>(
        Query query,
        Func<DbDataReader, T> map,
        QueryOptions? overlay,
        CancellationToken cancellationToken);

    Task<int> ExecuteAsync(
        Query query,
        QueryOptions? overlay,
        CancellationToken cancellationToken);

    IAsyncEnumerable<T> StreamAsync<T>(
        Query query,
        Func<DbDataReader, T> map,
        QueryOptions? overlay,
        CancellationToken cancellationToken);

    // Baked path: SQL was compiled at build time; values are still extracted
    // from the built query with the same deterministic parameterization pass.
    Task<IReadOnlyList<T>> QueryPrecompiledAsync<T>(
        string sql,
        Query query,
        Func<DbDataReader, T> map,
        QueryOptions? overlay,
        CancellationToken cancellationToken);
}
