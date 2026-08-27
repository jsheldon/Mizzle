namespace Mizzle.Tests;

public sealed class PgEmitterTests
{
    [Fact]
    public void Select_from_users_emits_quoted_postgres_sql()
    {
        var query = new SelectQuery(
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

        var sql = new PgEmitter().Emit(query, []);

        Assert.Equal("SELECT \"u\".\"email\" FROM \"public\".\"users\" AS \"u\"", sql.Sql);
        Assert.Empty(sql.Parameters);
    }

    [Fact]
    public void Where_eq_emits_parameterized_predicate()
    {
        var p = new ParamRef(0, typeof(string));
        var query = new SelectQuery(
            Select: [new SelectItem(new ColumnRef("u", "email", typeof(string)), null)],
            From: new FromSource("users", "public", "u"),
            Joins: [],
            Where: new BinaryExpr(BinaryOp.Eq, new ColumnRef("u", "email", typeof(string)), p),
            OrderBy: [],
            Limit: null,
            Offset: null,
            Distinct: false,
            With: [],
            RecursiveWith: false,
            UnionAll: []);

        var sql = new PgEmitter().Emit(query, ["a@b.com"]);

        Assert.Equal(
            "SELECT \"u\".\"email\" FROM \"public\".\"users\" AS \"u\" WHERE \"u\".\"email\" = $1",
            sql.Sql);
        Assert.Equal(["a@b.com"], sql.Parameters);
    }

    [Fact]
    public void Postgres_limit_offset()
    {
        var query = BaseSelect() with { Limit = 10, Offset = 20 };
        var sql = new PgEmitter().Emit(query, []);
        Assert.Equal(
            "SELECT \"u\".\"email\" FROM \"public\".\"users\" AS \"u\" LIMIT 10 OFFSET 20",
            sql.Sql);
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
        var sql = new PgEmitter().Emit(query, []);
        Assert.Equal(
            "SELECT DISTINCT \"u\".\"email\" FROM \"public\".\"users\" AS \"u\" INNER JOIN \"public\".\"posts\" AS \"p\" ON \"p\".\"user_id\" = \"u\".\"id\" ORDER BY \"u\".\"email\"",
            sql.Sql);
    }

    [Fact]
    public void Unparameterized_value_expr_throws_at_emit()
    {
        var query = BaseSelect() with
        {
            Where = new BinaryExpr(
                BinaryOp.Eq,
                new ColumnRef("u", "email", typeof(string)),
                new ValueExpr("x", typeof(string)))
        };
        Assert.Throws<InvalidOperationException>(() => new PgEmitter().Emit(query, []));
    }

    internal static SelectQuery BaseSelect() => new(
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
