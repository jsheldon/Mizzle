namespace Mizzle.Tests;

public sealed class WriteEmitterTests
{
    [Fact]
    public void Postgres_insert_returning()
    {
        var v = new ParamRef(0, typeof(string));
        var q = new InsertQuery(
            Into: new FromSource("users", "public", "u"),
            Columns: ["email"],
            ValuesRows: [[v]],
            FromSelect: null,
            Returning: [new SelectItem(new ColumnRef("u", "id", typeof(int)), null)],
            With: [],
            RecursiveWith: false);
        var sql = new PgEmitter().Emit(q, ["a@b.com"]);
        Assert.Equal(
            "INSERT INTO \"public\".\"users\" (\"email\") VALUES ($1) RETURNING \"u\".\"id\"",
            sql.Sql);
    }

    [Fact]
    public void SqlServer_insert_output()
    {
        var v = new ParamRef(0, typeof(string));
        var q = new InsertQuery(
            Into: new FromSource("users", "dbo", "u"),
            Columns: ["email"],
            ValuesRows: [[v]],
            FromSelect: null,
            Returning: [new SelectItem(new ColumnRef("u", "id", typeof(int)), null)],
            With: [],
            RecursiveWith: false);
        var sql = new SqlServerEmitter().Emit(q, ["a@b.com"]);
        Assert.Equal(
            "INSERT INTO [dbo].[users] ([email]) OUTPUT INSERTED.[id] VALUES (@p0)",
            sql.Sql);
    }

    [Fact]
    public void Postgres_update_set_where()
    {
        var email = new ParamRef(0, typeof(string));
        var id = new ParamRef(1, typeof(int));
        var q = new UpdateQuery(
            Table: new FromSource("users", "public", "u"),
            Set: [("email", email)],
            Where: new BinaryExpr(BinaryOp.Eq, new ColumnRef("u", "id", typeof(int)), id),
            Returning: [],
            With: [],
            RecursiveWith: false);
        var sql = new PgEmitter().Emit(q, ["b@c.com", 1]);
        Assert.Equal(
            "UPDATE \"public\".\"users\" AS \"u\" SET \"email\" = $1 WHERE \"u\".\"id\" = $2",
            sql.Sql);
    }

    [Fact]
    public void SqlServer_update_set_where()
    {
        var email = new ParamRef(0, typeof(string));
        var id = new ParamRef(1, typeof(int));
        var q = new UpdateQuery(
            Table: new FromSource("users", "dbo", "u"),
            Set: [("email", email)],
            Where: new BinaryExpr(BinaryOp.Eq, new ColumnRef("u", "id", typeof(int)), id),
            Returning: [],
            With: [],
            RecursiveWith: false);
        var sql = new SqlServerEmitter().Emit(q, ["b@c.com", 1]);
        Assert.Equal(
            "UPDATE [dbo].[users] SET [email] = @p0 WHERE [u].[id] = @p1",
            sql.Sql);
    }

    [Fact]
    public void Postgres_delete_where()
    {
        var id = new ParamRef(0, typeof(int));
        var q = new DeleteQuery(
            From: new FromSource("users", "public", "u"),
            Where: new BinaryExpr(BinaryOp.Eq, new ColumnRef("u", "id", typeof(int)), id),
            Returning: [],
            With: [],
            RecursiveWith: false);
        var sql = new PgEmitter().Emit(q, [1]);
        Assert.Equal(
            "DELETE FROM \"public\".\"users\" AS \"u\" WHERE \"u\".\"id\" = $1",
            sql.Sql);
    }

    [Fact]
    public void SqlServer_delete_where()
    {
        var id = new ParamRef(0, typeof(int));
        var q = new DeleteQuery(
            From: new FromSource("users", "dbo", "u"),
            Where: new BinaryExpr(BinaryOp.Eq, new ColumnRef("u", "id", typeof(int)), id),
            Returning: [],
            With: [],
            RecursiveWith: false);
        var sql = new SqlServerEmitter().Emit(q, [1]);
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
        var sql = new PgEmitter().Emit(q, []);
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
        var sql = new SqlServerEmitter().Emit(q, []);
        Assert.Equal(
            "WITH [stale] AS (SELECT [u].[id] FROM [dbo].[users] AS [u]) DELETE FROM [dbo].[users]",
            sql.Sql);
    }

    private static SelectQuery StagingSelect(string? schema) => new(
        Select: [new SelectItem(new ColumnRef("s", "email", typeof(string)), null)],
        From: new FromSource("staging", schema, "s"),
        Joins: [], Where: null, OrderBy: [], Limit: null, Offset: null,
        Distinct: false, With: [], RecursiveWith: false, UnionAll: []);

    [Fact]
    public void Postgres_insert_from_select()
    {
        var q = new InsertQuery(
            Into: new FromSource("users", "public", "u"),
            Columns: ["email"],
            ValuesRows: [],
            FromSelect: StagingSelect("public"),
            Returning: [],
            With: [],
            RecursiveWith: false);
        var sql = new PgEmitter().Emit(q, []);
        Assert.Equal(
            "INSERT INTO \"public\".\"users\" (\"email\") SELECT \"s\".\"email\" FROM \"public\".\"staging\" AS \"s\"",
            sql.Sql);
    }

    [Fact]
    public void SqlServer_insert_from_select_with_output()
    {
        var q = new InsertQuery(
            Into: new FromSource("users", "dbo", "u"),
            Columns: ["email"],
            ValuesRows: [],
            FromSelect: StagingSelect("dbo"),
            Returning: [new SelectItem(new ColumnRef("u", "id", typeof(int)), null)],
            With: [],
            RecursiveWith: false);
        var sql = new SqlServerEmitter().Emit(q, []);
        Assert.Equal(
            "INSERT INTO [dbo].[users] ([email]) OUTPUT INSERTED.[id] SELECT [s].[email] FROM [dbo].[staging] AS [s]",
            sql.Sql);
    }

    [Fact]
    public void Insert_with_values_and_select_throws()
    {
        var v = new ParamRef(0, typeof(string));
        var q = new InsertQuery(
            Into: new FromSource("users", "public", "u"),
            Columns: ["email"],
            ValuesRows: [[v]],
            FromSelect: StagingSelect("public"),
            Returning: [], With: [], RecursiveWith: false);
        Assert.Throws<InvalidOperationException>(() => new PgEmitter().Emit(q, ["x"]));
    }

    [Fact]
    public void Ilike_inside_from_select_throws_on_sql_server()
    {
        var p = new ParamRef(0, typeof(string));
        var source = StagingSelect("dbo") with
        {
            Where = new BinaryExpr(BinaryOp.ILike, new ColumnRef("s", "email", typeof(string)), p)
        };
        var q = new InsertQuery(
            Into: new FromSource("users", "dbo", "u"),
            Columns: ["email"],
            ValuesRows: [],
            FromSelect: source,
            Returning: [], With: [], RecursiveWith: false);
        var ex = Assert.Throws<UnsupportedFeatureException>(() => new SqlServerEmitter().Emit(q, ["%x%"]));
        Assert.Equal(Feature.ILike, ex.Feature);
    }
}
