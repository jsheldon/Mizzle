namespace Mizzle.Tests;

public sealed class ExprEmitTests
{
    [Fact]
    public void Like_in_between_count()
    {
        var bag = new ParamBag();
        var like = bag.Add("%a%", typeof(string));
        var query = PgEmitterTests.BaseSelect() with
        {
            Select = [new SelectItem(new AggregateExpr(AggregateKind.Count, null), "c")],
            Where = new BinaryExpr(BinaryOp.Like, new ColumnRef("u", "email", typeof(string)), like)
        };
        var sql = new PgEmitter().Emit(query, bag);
        Assert.Equal(
            "SELECT count(*) AS \"c\" FROM \"public\".\"users\" AS \"u\" WHERE \"u\".\"email\" LIKE $1",
            sql.Sql);
    }

    [Fact]
    public void SqlServer_rejects_postgres_function()
    {
        var query = PgEmitterTests.BaseSelect() with
        {
            Select = [new SelectItem(new CallExpr("lower", [new ColumnRef("u", "email", typeof(string))], DialectKind.Postgres), null)]
        };
        var ex = Assert.Throws<InvalidOperationException>(() => new SqlServerEmitter().Emit(query, new ParamBag()));
        Assert.Equal("Function 'lower' is not valid on SqlServer", ex.Message);
    }
}
