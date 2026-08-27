namespace Mizzle.Tests;

file sealed class Users : PgTable<Users>
{
    public Users() : base("users", "public") { }
    public PgColumn<int> Id { get; } = Identity("id").PrimaryKey();
    public PgColumn<string> Email { get; } = Text("email").NotNull();
}

public sealed class WriteBuilderTests
{
    private static (string Sql, IReadOnlyList<object?> Values) EmitPg(Query q)
    {
        var (canonical, values) = Parameterizer.Run(q);
        return (new PgEmitter().Emit(canonical, values).Sql, values);
    }

    private static SelectQuery StagingSelect() => new(
        Select: [new SelectItem(new ColumnRef("s", "email", typeof(string)), null)],
        From: new FromSource("staging", "public", "s"),
        Joins: [], Where: null, OrderBy: [], Limit: null, Offset: null,
        Distinct: false, With: [], RecursiveWith: false, UnionAll: []);

    [Fact]
    public void Insert_values_returning_builds_expected_pg_sql()
    {
        var users = new Users();
        var b = new InsertBuilder(users)
            .Value(users.Email, "a@b.com")
            .Returning(users.Id);
        var (sql, values) = EmitPg(b.Build());
        Assert.Equal(
            "INSERT INTO \"public\".\"users\" (\"email\") VALUES ($1) RETURNING \"users\".\"id\"",
            sql);
        Assert.Equal(["a@b.com"], values);
    }

    [Fact]
    public void Insert_two_rows_builds_two_value_tuples()
    {
        var users = new Users();
        var b = new InsertBuilder(users)
            .Value(users.Email, "a@b.com")
            .NewRow()
            .Value(users.Email, "c@d.com");
        var (sql, values) = EmitPg(b.Build());
        Assert.Equal(
            "INSERT INTO \"public\".\"users\" (\"email\") VALUES ($1), ($2)",
            sql);
    }

    [Fact]
    public void Insert_select_builds_insert_from_select()
    {
        var users = new Users();
        var b = new InsertBuilder(users).Select(StagingSelect(), users.Email);
        var (sql, values) = EmitPg(b.Build());
        Assert.Equal(
            "INSERT INTO \"public\".\"users\" (\"email\") SELECT \"s\".\"email\" FROM \"public\".\"staging\" AS \"s\"",
            sql);
    }

    [Fact]
    public void Insert_with_values_then_select_throws()
    {
        var users = new Users();
        var b = new InsertBuilder(users).Value(users.Email, "x");
        Assert.Throws<InvalidOperationException>(() => b.Select(StagingSelect(), users.Email));
    }

    [Fact]
    public void Insert_row_with_different_columns_throws()
    {
        var users = new Users();
        var b = new InsertBuilder(users)
            .Value(users.Email, "a@b.com")
            .NewRow();
        Assert.Throws<InvalidOperationException>(() => b.Value(users.Id, 5).Build());
    }

    [Fact]
    public void Delete_where_returning_builds_expected_pg_sql()
    {
        var users = new Users();
        var b = new DeleteBuilder(users)
            .Where(users.Email, "a@b.com")
            .Returning(users.Id);
        var (sql, values) = EmitPg(b.Build());
        Assert.Equal(
            "DELETE FROM \"public\".\"users\" AS \"users\" WHERE \"users\".\"email\" = $1 RETURNING \"users\".\"id\"",
            sql);
    }

    [Fact]
    public void Update_returning_flows_into_query()
    {
        var users = new Users();
        var b = new UpdateBuilder(users)
            .Set(users.Email, "new@b.com")
            .Where(users.Id, 1)
            .Returning(users.Email);
        var q = b.Build();
        var item = Assert.Single(q.Returning);
        Assert.Equal(new ColumnRef("users", "email", typeof(string)), item.Expr);
    }
}
