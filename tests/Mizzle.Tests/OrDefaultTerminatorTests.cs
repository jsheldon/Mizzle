using System.Data.Common;

namespace Mizzle.Tests;

file sealed class RowsExecutor : IQueryExecutor
{
    public IReadOnlyList<string> Rows = [];

    public Task<IReadOnlyList<T>> QueryAsync<T>(Query q, Func<DbDataReader, T> m, QueryOptions? o, CancellationToken c)
        => Task.FromResult((IReadOnlyList<T>)(object)Rows);

    public Task<int> ExecuteAsync(Query q, QueryOptions? o, CancellationToken c)
        => throw new NotSupportedException();

    public IAsyncEnumerable<T> StreamAsync<T>(Query q, Func<DbDataReader, T> m, QueryOptions? o, CancellationToken c)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<T>> QueryPrecompiledAsync<T>(string s, Query q, Func<DbDataReader, T> m, QueryOptions? o, CancellationToken c)
        => throw new NotSupportedException();
}

public sealed class OrDefaultTerminatorTests
{
    private static SelectBuilder Builder(IQueryExecutor exec)
        => new SelectBuilder(exec)
            .Select(new ColumnRef("u", "email", typeof(string)))
            .From(new FromSource("users", "public", "u"));

    [Fact]
    public async Task FirstOrDefault_returns_null_on_empty()
    {
        var result = await Builder(new RowsExecutor()).FirstOrDefaultAsync(r => r.GetString(0));
        Assert.Null(result);
    }

    [Fact]
    public async Task FirstOrDefault_returns_first_row()
    {
        var exec = new RowsExecutor { Rows = ["a", "b"] };
        Assert.Equal("a", await Builder(exec).FirstOrDefaultAsync(r => r.GetString(0)));
    }

    [Fact]
    public async Task SingleOrDefault_returns_null_on_empty()
    {
        Assert.Null(await Builder(new RowsExecutor()).SingleOrDefaultAsync(r => r.GetString(0)));
    }

    [Fact]
    public async Task SingleOrDefault_returns_single_row()
    {
        var exec = new RowsExecutor { Rows = ["a"] };
        Assert.Equal("a", await Builder(exec).SingleOrDefaultAsync(r => r.GetString(0)));
    }

    [Fact]
    public async Task SingleOrDefault_throws_on_two_rows()
    {
        var exec = new RowsExecutor { Rows = ["a", "b"] };
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Builder(exec).SingleOrDefaultAsync(r => r.GetString(0)));
    }
}
