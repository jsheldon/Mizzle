namespace Mizzle.Tests;

public sealed class ParameterizerTests
{
    private static SelectQuery Build(string email)
        => new(
            Select: [new SelectItem(new ColumnRef("u", "email", typeof(string)), null)],
            From: new FromSource("users", "public", "u"),
            Joins:
            [
                new JoinClause(
                    JoinKind.Left,
                    new FromSource("lists", "public", "c"),
                    new BinaryExpr(
                        BinaryOp.Eq,
                        new ColumnRef("c", "type", typeof(string)),
                        new ValueExpr("language", typeof(string))))
            ],
            Where: new BinaryExpr(
                BinaryOp.Eq,
                new ColumnRef("u", "email", typeof(string)),
                new ValueExpr(email, typeof(string))),
            OrderBy: [], Limit: null, Offset: null, Distinct: false,
            With: [], RecursiveWith: false, UnionAll: []);

    [Fact]
    public void Assigns_slots_in_join_then_where_order()
    {
        var (canonical, values) = Parameterizer.Run(Build("a@b.com"));
        Assert.Equal(["language", "a@b.com"], values);
        var select = Assert.IsType<SelectQuery>(canonical);
        var joinRight = Assert.IsType<BinaryExpr>(select.Joins[0].On).Right;
        Assert.Equal(new ParamRef(0, typeof(string)), joinRight);
        var whereRight = Assert.IsType<BinaryExpr>(select.Where!).Right;
        Assert.Equal(new ParamRef(1, typeof(string)), whereRight);
    }

    [Fact]
    public void Same_shape_different_values_produce_equal_canonical_queries()
    {
        var (a, _) = Parameterizer.Run(Build("a@b.com"));
        var (b, _) = Parameterizer.Run(Build("zzz@zzz.org"));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Insert_values_rows_are_row_major()
    {
        var q = new InsertQuery(
            Into: new FromSource("users", "public", "u"),
            Columns: ["email", "name"],
            ValuesRows:
            [
                [new ValueExpr("e1", typeof(string)), new ValueExpr("n1", typeof(string))],
                [new ValueExpr("e2", typeof(string)), new ValueExpr("n2", typeof(string))]
            ],
            FromSelect: null, Returning: [], With: [], RecursiveWith: false);
        var (_, values) = Parameterizer.Run(q);
        Assert.Equal(["e1", "n1", "e2", "n2"], values);
    }

    [Fact]
    public void Preexisting_paramref_throws()
    {
        var q = Build("x") with
        {
            Where = new BinaryExpr(
                BinaryOp.Eq,
                new ColumnRef("u", "email", typeof(string)),
                new ParamRef(0, typeof(string)))
        };
        Assert.Throws<InvalidOperationException>(() => Parameterizer.Run(q));
    }
}
