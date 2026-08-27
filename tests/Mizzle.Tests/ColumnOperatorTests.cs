namespace Mizzle.Tests;

file sealed class Users : PgTable<Users>
{
    public Users() : base("users", "public") { }
    public PgColumn<int> Id { get; } = Identity("id").PrimaryKey();
    public PgColumn<string> Email { get; } = Text("email").NotNull();
    public PgColumn<int> Age { get; } = Integer("age");
}

public sealed class ColumnOperatorTests
{
    private static string EmitWhere(Expr where)
    {
        var q = new SelectQuery(
            Select: [new SelectItem(new ColumnRef("u", "id", typeof(int)), null)],
            From: new FromSource("users", "public", "u"),
            Joins: [], Where: where, OrderBy: [], Limit: null, Offset: null,
            Distinct: false, With: [], RecursiveWith: false, UnionAll: []);
        var (canonical, values) = Parameterizer.Run(q);
        return new PgEmitter().Emit(canonical, values).Sql;
    }

    [Fact]
    public void Eq_value_and_column_forms()
    {
        var u = new Users().WithAlias("u");
        Assert.EndsWith("WHERE \"u\".\"email\" = $1", EmitWhere(u.Email.Eq("a@b.com")), StringComparison.Ordinal);
        Assert.EndsWith("WHERE \"u\".\"id\" = \"u\".\"age\"", EmitWhere(u.Id.Eq(u.Age)), StringComparison.Ordinal);
    }

    [Fact]
    public void Comparison_null_in_between_like()
    {
        var u = new Users().WithAlias("u");
        Assert.EndsWith("\"u\".\"age\" > $1", EmitWhere(u.Age.Gt(21)), StringComparison.Ordinal);
        Assert.EndsWith("\"u\".\"age\" >= $1", EmitWhere(u.Age.Gte(21)), StringComparison.Ordinal);
        Assert.EndsWith("\"u\".\"age\" < $1", EmitWhere(u.Age.Lt(65)), StringComparison.Ordinal);
        Assert.EndsWith("\"u\".\"age\" <= $1", EmitWhere(u.Age.Lte(65)), StringComparison.Ordinal);
        Assert.EndsWith("\"u\".\"age\" <> $1", EmitWhere(u.Age.Ne(0)), StringComparison.Ordinal);
        Assert.EndsWith("\"u\".\"email\" IS NULL", EmitWhere(u.Email.IsNull()), StringComparison.Ordinal);
        Assert.EndsWith("\"u\".\"email\" IS NOT NULL", EmitWhere(u.Email.IsNotNull()), StringComparison.Ordinal);
        Assert.EndsWith("\"u\".\"age\" IN ($1, $2)", EmitWhere(u.Age.In(1, 2)), StringComparison.Ordinal);
        Assert.EndsWith("\"u\".\"age\" BETWEEN $1 AND $2", EmitWhere(u.Age.Between(18, 65)), StringComparison.Ordinal);
        Assert.EndsWith("\"u\".\"email\" LIKE $1", EmitWhere(u.Email.Like("%a%")), StringComparison.Ordinal);
        Assert.EndsWith("\"u\".\"email\" ILIKE $1", EmitWhere(u.Email.ILike("%a%")), StringComparison.Ordinal);
    }

    [Fact]
    public void Variadic_and_folds_left()
    {
        var u = new Users().WithAlias("u");
        Assert.EndsWith(
            "WHERE ((\"u\".\"id\" = $1 AND \"u\".\"age\" > $2) AND \"u\".\"email\" = $3)",
            EmitWhere(Sql.And(u.Id.Eq(1), u.Age.Gt(2), u.Email.Eq("x"))),
            StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Sql.And(u.Id.Eq(1)));
    }

    [Fact]
    public void Variadic_where_and_combines()
    {
        var u = new Users();
        var built = new SelectBuilder()
            .Select(u.Id)
            .From(u.ToFrom())
            .Where(u.Id.Eq(1), u.Age.Gt(2))
            .Build();
        Assert.NotNull(built.Where);
        Assert.Equal(BinaryOp.And, Assert.IsType<BinaryExpr>(built.Where!).Op);
    }
}
