namespace Mizzle.Tests;

file sealed class SqlVocab : SqlTable<SqlVocab>
{
    public SqlVocab() : base("revdel0", "dbo") { }
    public SqlColumn<string> Code { get; } = VarChar("code", 50);
    public SqlColumn<decimal> TypeId { get; } = Numeric("type_id").NotNull();
}

// A column should be usable anywhere an Expr is expected without an explicit
// .ToRef() -- mixing a plain column and a computed expression in the same call
// (e.g. PartitionBy) is the case that most needs this.
public sealed class ColumnExpressionConsistencyTests
{
    private static CompiledSql Compile(SelectBuilder builder)
    {
        var (canonical, values) = Parameterizer.Run(builder.Build());
        return new SqlServerEmitter().Emit(canonical, values);
    }

    [Fact]
    public void A_column_converts_implicitly_where_an_expression_is_expected()
    {
        var v = new SqlVocab();

        // LTrim/Upper/Lower never had an IColumn overload the way RTrim did --
        // this is only reachable at all once Column<T> converts to Expr.
        var sql = Compile(new SelectBuilder()
            .Select(Sql.As(TSql.LTrim(v.Code), "trimmed"))
            .From(v.ToFrom()));

        Assert.Equal(
            "SELECT ltrim([revdel0].[code]) AS [trimmed] FROM [dbo].[revdel0] AS [revdel0]",
            sql.Sql);
    }

    [Fact]
    public void A_column_and_a_computed_expression_mix_in_one_partition_by_call()
    {
        var v = new SqlVocab();

        // Previously required v.Code.ToRef() to share an array with TSql.RTrim(...).
        var sql = Compile(new SelectBuilder()
            .Select(Sql.As(Sql.RowNumber()
                .PartitionBy(v.Code, TSql.RTrim(v.Code))
                .OrderBy(v.TypeId), "rn"))
            .From(v.ToFrom()));

        Assert.Equal(
            "SELECT ROW_NUMBER() OVER (PARTITION BY [revdel0].[code], rtrim([revdel0].[code]) "
            + "ORDER BY [revdel0].[type_id]) AS [rn] FROM [dbo].[revdel0] AS [revdel0]",
            sql.Sql);
    }

    [Fact]
    public void Sql_As_accepts_a_column_the_same_way_it_accepts_an_expression()
    {
        var v = new SqlVocab();
        var sql = Compile(new SelectBuilder()
            .Select(Sql.As(v.Code, "c"))
            .From(v.ToFrom()));

        Assert.Equal(
            "SELECT [revdel0].[code] AS [c] FROM [dbo].[revdel0] AS [revdel0]",
            sql.Sql);
    }
}
