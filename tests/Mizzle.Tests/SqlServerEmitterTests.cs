namespace Mizzle.Tests;

public sealed class SqlServerEmitterTests
{
    [Fact]
    public void SqlServer_offset_fetch()
    {
        var query = BaseSelect() with
        {
            Limit = 10,
            Offset = 20,
            OrderBy = [new OrderByItem(new ColumnRef("u", "email", typeof(string)), Descending: false)]
        };
        var sql = new SqlServerEmitter().Emit(query, []);
        Assert.Equal(
            "SELECT [u].[email] FROM [public].[users] AS [u] ORDER BY [u].[email] OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY",
            sql.Sql);
    }

    [Fact]
    public void SqlServer_offset_only_returns_remainder()
    {
        var query = BaseSelect() with
        {
            Offset = 20,
            OrderBy = [new OrderByItem(new ColumnRef("u", "email", typeof(string)), Descending: false)]
        };
        var sql = new SqlServerEmitter().Emit(query, []);
        Assert.Equal(
            "SELECT [u].[email] FROM [public].[users] AS [u] ORDER BY [u].[email] OFFSET 20 ROWS",
            sql.Sql);
    }

    [Fact]
    public void SqlServer_paging_without_order_by_throws()
    {
        var query = BaseSelect() with { Limit = 10 };
        var ex = Assert.Throws<InvalidOperationException>(
            () => new SqlServerEmitter().Emit(query, []));
        Assert.Contains("ORDER BY", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlServer_ilike_throws()
    {
        var p = new ParamRef(0, typeof(string));
        var query = BaseSelect() with
        {
            Where = new BinaryExpr(BinaryOp.ILike, new ColumnRef("u", "email", typeof(string)), p)
        };
        var ex = Assert.Throws<UnsupportedFeatureException>(
            () => new SqlServerEmitter().Emit(query, ["%x%"]));
        Assert.Equal(Feature.ILike, ex.Feature);
        Assert.Equal(DialectKind.SqlServer, ex.Dialect);
    }

    [Fact]
    public void Inner_join_order_distinct()
    {
        var query = BaseSelect() with
        {
            Distinct = true,
            Joins =
            [
                new JoinClause(
                    JoinKind.Inner,
                    new FromSource("posts", "public", "p"),
                    new BinaryExpr(
                        BinaryOp.Eq,
                        new ColumnRef("p", "user_id", typeof(int)),
                        new ColumnRef("u", "id", typeof(int))))
            ],
            OrderBy = [new OrderByItem(new ColumnRef("u", "email", typeof(string)), Descending: false)]
        };
        var sql = new SqlServerEmitter().Emit(query, []);
        Assert.Equal(
            "SELECT DISTINCT [u].[email] FROM [public].[users] AS [u] INNER JOIN [public].[posts] AS [p] ON [p].[user_id] = [u].[id] ORDER BY [u].[email]",
            sql.Sql);
    }

    [Fact]
    public void Nested_TSql_Convert_emits_CONVERT()
    {
        var query = BaseSelect() with
        {
            Where = new BinaryExpr(
                BinaryOp.Eq,
                new ColumnRef("r", "evd_fdb_vocab_id", typeof(string)),
                TSql.Convert(SqlType.VarChar(20), TSql.Convert(SqlType.Int, new ColumnRef("pm", "medid", typeof(int)))))
        };
        var sql = new SqlServerEmitter().Emit(query, []);
        Assert.Equal(
            "SELECT [u].[email] FROM [public].[users] AS [u] WHERE [r].[evd_fdb_vocab_id] = CONVERT(varchar(20), CONVERT(int, [pm].[medid]))",
            sql.Sql);
    }

    private static SelectQuery BaseSelect() => new(
        Select: [new SelectItem(new ColumnRef("u", "email", typeof(string)), null)],
        From: new FromSource("users", "public", "u"),
        Joins: [],
        Where: null,
        OrderBy: [],
        Limit: null,
        Offset: null,
        Distinct: false,
        With: [],
        RecursiveWith: false,
        UnionAll: []);
}
