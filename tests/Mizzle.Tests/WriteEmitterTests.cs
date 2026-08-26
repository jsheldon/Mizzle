namespace Mizzle.Tests;

public sealed class WriteEmitterTests
{
    [Fact]
    public void Postgres_insert_returning()
    {
        var bag = new ParamBag();
        var v = bag.Add("a@b.com", typeof(string));
        var q = new InsertQuery(
            Into: new FromSource("users", "public", "u"),
            Columns: ["email"],
            ValuesRows: [[v]],
            FromSelect: null,
            Returning: [new SelectItem(new ColumnRef("u", "id", typeof(int)), null)],
            With: [],
            RecursiveWith: false);
        var sql = new PgEmitter().Emit(q, bag);
        Assert.Equal(
            "INSERT INTO \"public\".\"users\" (\"email\") VALUES ($1) RETURNING \"u\".\"id\"",
            sql.Sql);
    }

    [Fact]
    public void SqlServer_insert_output()
    {
        var bag = new ParamBag();
        var v = bag.Add("a@b.com", typeof(string));
        var q = new InsertQuery(
            Into: new FromSource("users", "dbo", "u"),
            Columns: ["email"],
            ValuesRows: [[v]],
            FromSelect: null,
            Returning: [new SelectItem(new ColumnRef("u", "id", typeof(int)), null)],
            With: [],
            RecursiveWith: false);
        var sql = new SqlServerEmitter().Emit(q, bag);
        Assert.Equal(
            "INSERT INTO [dbo].[users] ([email]) OUTPUT INSERTED.[id] VALUES (@p0)",
            sql.Sql);
    }

    [Fact]
    public void Postgres_update_set_where()
    {
        var bag = new ParamBag();
        var email = bag.Add("b@c.com", typeof(string));
        var id = bag.Add(1, typeof(int));
        var q = new UpdateQuery(
            Table: new FromSource("users", "public", "u"),
            Set: [("email", email)],
            Where: new BinaryExpr(BinaryOp.Eq, new ColumnRef("u", "id", typeof(int)), id),
            Returning: [],
            With: [],
            RecursiveWith: false);
        var sql = new PgEmitter().Emit(q, bag);
        Assert.Equal(
            "UPDATE \"public\".\"users\" AS \"u\" SET \"email\" = $1 WHERE \"u\".\"id\" = $2",
            sql.Sql);
    }

    [Fact]
    public void SqlServer_update_set_where()
    {
        var bag = new ParamBag();
        var email = bag.Add("b@c.com", typeof(string));
        var id = bag.Add(1, typeof(int));
        var q = new UpdateQuery(
            Table: new FromSource("users", "dbo", "u"),
            Set: [("email", email)],
            Where: new BinaryExpr(BinaryOp.Eq, new ColumnRef("u", "id", typeof(int)), id),
            Returning: [],
            With: [],
            RecursiveWith: false);
        var sql = new SqlServerEmitter().Emit(q, bag);
        Assert.Equal(
            "UPDATE [dbo].[users] SET [email] = @p0 WHERE [u].[id] = @p1",
            sql.Sql);
    }

    [Fact]
    public void Postgres_delete_where()
    {
        var bag = new ParamBag();
        var id = bag.Add(1, typeof(int));
        var q = new DeleteQuery(
            From: new FromSource("users", "public", "u"),
            Where: new BinaryExpr(BinaryOp.Eq, new ColumnRef("u", "id", typeof(int)), id),
            Returning: [],
            With: [],
            RecursiveWith: false);
        var sql = new PgEmitter().Emit(q, bag);
        Assert.Equal(
            "DELETE FROM \"public\".\"users\" AS \"u\" WHERE \"u\".\"id\" = $1",
            sql.Sql);
    }

    [Fact]
    public void SqlServer_delete_where()
    {
        var bag = new ParamBag();
        var id = bag.Add(1, typeof(int));
        var q = new DeleteQuery(
            From: new FromSource("users", "dbo", "u"),
            Where: new BinaryExpr(BinaryOp.Eq, new ColumnRef("u", "id", typeof(int)), id),
            Returning: [],
            With: [],
            RecursiveWith: false);
        var sql = new SqlServerEmitter().Emit(q, bag);
        Assert.Equal(
            "DELETE FROM [dbo].[users] WHERE [u].[id] = @p0",
            sql.Sql);
    }

    [Fact]
    public void Postgres_delete_with_cte()
    {
        var cteBody = new SelectQuery(
            Select: [new SelectItem(new ColumnRef("u", "id", typeof(int)), null)],
            From: new FromSource("users", "public", "u"),
            Joins: [], Where: null, OrderBy: [], Limit: null, Offset: null,
            Distinct: false, With: [], RecursiveWith: false, UnionAll: []);
        var q = new DeleteQuery(
            From: new FromSource("users", "public", "u"),
            Where: null,
            Returning: [],
            With: [new CteClause("stale", cteBody)],
            RecursiveWith: false);
        var sql = new PgEmitter().Emit(q, new ParamBag());
        Assert.Equal(
            "WITH \"stale\" AS (SELECT \"u\".\"id\" FROM \"public\".\"users\" AS \"u\") DELETE FROM \"public\".\"users\" AS \"u\"",
            sql.Sql);
    }

    [Fact]
    public void SqlServer_delete_with_cte()
    {
        var cteBody = new SelectQuery(
            Select: [new SelectItem(new ColumnRef("u", "id", typeof(int)), null)],
            From: new FromSource("users", "dbo", "u"),
            Joins: [], Where: null, OrderBy: [], Limit: null, Offset: null,
            Distinct: false, With: [], RecursiveWith: false, UnionAll: []);
        var q = new DeleteQuery(
            From: new FromSource("users", "dbo", "u"),
            Where: null,
            Returning: [],
            With: [new CteClause("stale", cteBody)],
            RecursiveWith: false);
        var sql = new SqlServerEmitter().Emit(q, new ParamBag());
        Assert.Equal(
            "WITH [stale] AS (SELECT [u].[id] FROM [dbo].[users] AS [u]) DELETE FROM [dbo].[users]",
            sql.Sql);
    }
}
