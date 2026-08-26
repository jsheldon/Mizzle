using System.Data.Common;

namespace Mizzle.Tests;

file sealed class Users : PgTable<Users>
{
    public Users() : base("users", "public", "u") { }
    public PgColumn<string> Email { get; } = Text("email").NotNull();
}

file sealed class RecordingExecutor : IQueryExecutor
{
    public string? PrecompiledSql;
    public IReadOnlyList<object?>? PrecompiledValues;

    public Task<IReadOnlyList<T>> QueryPrecompiledAsync<T>(
        string sql, ParamBag bag, Func<DbDataReader, T> map, QueryOptions? overlay, CancellationToken ct)
    {
        PrecompiledSql = sql;
        PrecompiledValues = bag.Values;
        return Task.FromResult<IReadOnlyList<T>>([]);
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(Query q, ParamBag b, Func<DbDataReader, T> m, QueryOptions? o, CancellationToken c)
        => throw new InvalidOperationException("runtime path must not run");

    public Task<int> ExecuteAsync(Query q, ParamBag b, QueryOptions? o, CancellationToken c)
        => throw new InvalidOperationException("runtime path must not run");

    public IAsyncEnumerable<T> StreamAsync<T>(Query q, ParamBag b, Func<DbDataReader, T> m, QueryOptions? o, CancellationToken c)
        => throw new InvalidOperationException("runtime path must not run");
}

public sealed class PrecompiledSeamTests
{
    [Fact]
    public async Task Precompiled_terminator_bypasses_ir_pipeline()
    {
        var exec = new RecordingExecutor();
        var users = new Users();
        var builder = new SelectBuilder(new ParamBag(), exec)
            .Select(users.Email)
            .From(users.ToFrom())
            .Where(users.Email, "a@b.com");
        _ = await builder.ToListPrecompiledAsync("SELECT 1", r => r.GetInt32(0));
        Assert.Equal("SELECT 1", exec.PrecompiledSql);
        Assert.Equal(["a@b.com"], exec.PrecompiledValues);
    }
}
