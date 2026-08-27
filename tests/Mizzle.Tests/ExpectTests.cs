namespace Mizzle.Tests;

file sealed class VersionedUsers : PgTable<VersionedUsers>
{
    public VersionedUsers() : base("users", "public") { }

    public PgColumn<int> Id { get; } = Identity("id");
    public PgColumn<string> Email { get; } = Text("email");
    public PgColumn<int> RowVersion { get; } = Integer("version").Version();
}

public sealed class ExpectTests
{
    [Fact]
    public void Expect_mismatch_throws_concurrency_exception()
    {
        var ex = new ConcurrencyException(1, 0);
        Assert.Equal(1, ex.Expected);
        Assert.Equal(0, ex.Actual);
    }

    [Fact]
    public void SqlServer_lock_emits_sp_getapplock()
    {
        var sql = new SqlServerEmitter().Emit(new LockQuery("k"), ["k"]);
        Assert.Contains("sp_getapplock", sql.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Postgres_lock_emits_advisory_lock()
    {
        var sql = new PgEmitter().Emit(new LockQuery("k"), ["k"]);
        Assert.Equal("SELECT pg_advisory_xact_lock(hashtext($1))", sql.Sql);
    }

    [Fact]
    public void Version_column_missing_from_where_throws()
    {
        var table = new VersionedUsers();
        var builder = new UpdateBuilder(table)
            .Set(table.Email, "x")
            .Where(table.Id, 1);
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Equal("Version column must appear in WHERE", ex.Message);
    }

    [Fact]
    public void Version_column_in_where_allows_build()
    {
        var table = new VersionedUsers();
        var query = new UpdateBuilder(table)
            .Set(table.Email, "x")
            .Where(table.Id, 1)
            .Where(table.RowVersion, 0)
            .Build();
        Assert.NotNull(query.Where);
    }
}
