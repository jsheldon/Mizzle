using System.Data.Common;

namespace Mizzle.Tests;

file sealed class Users : PgTable<Users>
{
    public Users() : base("users", "public", "u") { }
    public PgColumn<string> Email { get; } = Text("email").NotNull();
}

file sealed class CapturingExecutor : IQueryExecutor
{
    public Query? Captured;
    public string? PrecompiledSql;
    public IReadOnlyList<object?>? PrecompiledValues;

    public Task<IReadOnlyList<T>> QueryAsync<T>(Query q, Func<DbDataReader, T> m, QueryOptions? o, CancellationToken c)
    {
        Captured = q;
        return Task.FromResult<IReadOnlyList<T>>([]);
    }

    public Task<int> ExecuteAsync(Query q, QueryOptions? o, CancellationToken c) => Task.FromResult(0);

    public IAsyncEnumerable<T> StreamAsync<T>(Query q, Func<DbDataReader, T> m, QueryOptions? o, CancellationToken c)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<T>> QueryPrecompiledAsync<T>(string sql, Query q, Func<DbDataReader, T> m, QueryOptions? o, CancellationToken c)
    {
        PrecompiledSql = sql;
        PrecompiledValues = Parameterizer.Run(q).Values;
        return Task.FromResult<IReadOnlyList<T>>([]);
    }
}

public sealed class ExecutorPipelineTests
{
    [Fact]
    public async Task Builder_needs_no_bag_and_embeds_values()
    {
        var exec = new CapturingExecutor();
        var users = new Users();
        _ = await new SelectBuilder(exec)
            .Select(users.Email)
            .From(users.ToFrom())
            .Where(users.Email, "a@b.com")
            .ToListAsync(r => r.GetString(0));

        var q = Assert.IsType<SelectQuery>(exec.Captured);
        var (canonical, values) = Parameterizer.Run(q);
        Assert.Equal(["a@b.com"], values);
        var where = Assert.IsType<BinaryExpr>(Assert.IsType<SelectQuery>(canonical).Where);
        Assert.Equal(new ParamRef(0, typeof(string)), where.Right);
    }

    [Fact]
    public async Task Precompiled_path_extracts_values_from_built_query()
    {
        var exec = new CapturingExecutor();
        var users = new Users();
        var builder = new SelectBuilder(exec)
            .Select(users.Email)
            .From(users.ToFrom())
            .Where(users.Email, "a@b.com");
        _ = await builder.ToListPrecompiledAsync("SELECT 1", r => r.GetString(0));
        Assert.Equal("SELECT 1", exec.PrecompiledSql);
        Assert.Equal(["a@b.com"], exec.PrecompiledValues);
    }
}
