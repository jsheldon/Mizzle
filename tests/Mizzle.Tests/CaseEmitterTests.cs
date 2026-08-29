namespace Mizzle.Tests;

file sealed class PgVocab : PgTable<PgVocab>
{
    public PgVocab() : base("revdel0", "public") { }
    public PgColumn<string> Code { get; } = Text("code");
    public PgColumn<decimal> TypeId { get; } = Numeric("type_id").NotNull();
}

file sealed class SqlVocab : SqlTable<SqlVocab>
{
    public SqlVocab() : base("revdel0", "dbo") { }
    public SqlColumn<string> Code { get; } = VarChar("code", 50);
    public SqlColumn<decimal> TypeId { get; } = Numeric("type_id").NotNull();
}

// CASE is standard on both dialects, so the only thing that differs is quoting
// and placeholder style.
public sealed class CaseEmitterTests
{
    private static CompiledSql Compile(ISqlEmitter emitter, SelectBuilder builder)
    {
        var (canonical, values) = Parameterizer.Run(builder.Build());
        return emitter.Emit(canonical, values);
    }

    [Fact]
    public void Case_emits_searched_form_on_sql_server()
    {
        var v = new SqlVocab();
        var sql = Compile(new SqlServerEmitter(), new SelectBuilder()
            .Select(Sql.As(Sql.Case(
                    Sql.When(v.TypeId.Eq(504m), 0),
                    Sql.When(v.TypeId.Eq(502m), 1))
                .Else(Sql.Value(4)), "pri"))
            .From(v.ToFrom()));

        Assert.Equal(
            "SELECT CASE WHEN [revdel0].[type_id] = @p0 THEN @p1 WHEN [revdel0].[type_id] = @p2 "
            + "THEN @p3 ELSE @p4 END AS [pri] FROM [dbo].[revdel0] AS [revdel0]",
            sql.Sql);
        Assert.Equal<object?[]>([504m, 0, 502m, 1, 4], [..sql.Parameters]);
    }

    [Fact]
    public void Case_emits_searched_form_on_postgres()
    {
        var v = new PgVocab();
        var sql = Compile(new PgEmitter(), new SelectBuilder()
            .Select(Sql.As(Sql.Case(Sql.When(v.TypeId.Eq(504m), 0)), "pri"))
            .From(v.ToFrom()));

        Assert.Equal(
            "SELECT CASE WHEN \"revdel0\".\"type_id\" = $1 THEN $2 END AS \"pri\" "
            + "FROM \"public\".\"revdel0\" AS \"revdel0\"",
            sql.Sql);
    }

    [Fact]
    public void Case_arms_are_parameterized_in_emitted_order()
    {
        var v = new SqlVocab();
        var sql = Compile(new SqlServerEmitter(), new SelectBuilder()
            .Select(v.Code, Sql.As(Sql.Case(Sql.When(v.TypeId.Eq(7m), "seven")).Else(Sql.Value("other")), "label"))
            .From(v.ToFrom())
            .Where(v.Code.Eq("z")));

        // Select items are captured before the WHERE, and within the CASE each
        // arm's condition is captured before its result.
        Assert.Equal<object?[]>([7m, "seven", "other", "z"], [..sql.Parameters]);
    }

    [Fact]
    public void A_case_needs_at_least_one_arm()
    {
        Assert.Throws<ArgumentException>(() => Sql.Case());
    }

    // Sql.Value binds its argument, so handing it an expression would emit a
    // placeholder where the caller meant the expression's SQL.
    [Fact]
    public void Value_refuses_an_expression()
    {
        var v = new SqlVocab();
        Assert.Throws<ArgumentException>(() => Sql.Value(TSql.RTrim(v.Code)));
    }

    [Fact]
    public void In_emits_one_placeholder_per_value()
    {
        var v = new SqlVocab();
        var sql = Compile(new SqlServerEmitter(), new SelectBuilder()
            .Select(v.Code)
            .From(v.ToFrom())
            .Where(v.TypeId.In(504m, 502m, 503m)));

        Assert.Equal(
            "SELECT [revdel0].[code] FROM [dbo].[revdel0] AS [revdel0] "
            + "WHERE [revdel0].[type_id] IN (@p0, @p1, @p2)",
            sql.Sql);
        Assert.Equal<object?[]>([504m, 502m, 503m], [..sql.Parameters]);
    }
}
