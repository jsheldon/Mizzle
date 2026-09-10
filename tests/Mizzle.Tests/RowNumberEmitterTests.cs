namespace Mizzle.Tests;

file sealed class SqlVocab : SqlTable<SqlVocab>
{
    public SqlVocab() : base("revdel0", "dbo") { }
    public SqlColumn<string> VocabId { get; } = VarChar("vocab_id", 20).NotNull();
    public SqlColumn<string> Code { get; } = VarChar("code", 50);
    public SqlColumn<decimal> TypeId { get; } = Numeric("type_id").NotNull();
}

file sealed class PgVocab : PgTable<PgVocab>
{
    public PgVocab() : base("revdel0", "public") { }
    public PgColumn<string> VocabId { get; } = Text("vocab_id").NotNull();
    public PgColumn<string> Code { get; } = Text("code");
    public PgColumn<decimal> TypeId { get; } = Numeric("type_id").NotNull();
}

// ROW_NUMBER() is standard on both dialects, so only quoting differs; the
// SQL Server half of that lives here, the PostgreSQL half in Task 2.
public sealed class RowNumberEmitterTests
{
    private static CompiledSql Compile(ISqlEmitter emitter, SelectBuilder builder)
    {
        var (canonical, values) = Parameterizer.Run(builder.Build());
        return emitter.Emit(canonical, values);
    }

    [Fact]
    public void RowNumber_emits_partition_and_order_on_sql_server()
    {
        var v = new SqlVocab();
        var sql = Compile(new SqlServerEmitter(), new SelectBuilder()
            .Select(Sql.As(Sql.RowNumber()
                .PartitionBy(v.VocabId, v.TypeId)
                .OrderBy(v.Code)
                .OrderByDesc(v.TypeId), "rn"))
            .From(v.ToFrom()));

        Assert.Equal(
            "SELECT ROW_NUMBER() OVER (PARTITION BY [revdel0].[vocab_id], [revdel0].[type_id] "
            + "ORDER BY [revdel0].[code], [revdel0].[type_id] DESC) AS [rn] "
            + "FROM [dbo].[revdel0] AS [revdel0]",
            sql.Sql);
    }

    [Fact]
    public void RowNumber_without_partition_omits_the_clause()
    {
        var v = new SqlVocab();
        var sql = Compile(new SqlServerEmitter(), new SelectBuilder()
            .Select(Sql.As(Sql.RowNumber().OrderBy(v.Code), "rn"))
            .From(v.ToFrom()));

        Assert.Equal(
            "SELECT ROW_NUMBER() OVER (ORDER BY [revdel0].[code]) AS [rn] "
            + "FROM [dbo].[revdel0] AS [revdel0]",
            sql.Sql);
    }

    [Fact]
    public void RowNumber_needs_at_least_one_order_column()
    {
        var v = new SqlVocab();
        var builder = new SelectBuilder()
            .Select(Sql.As(Sql.RowNumber().PartitionBy(v.VocabId), "rn"))
            .From(v.ToFrom());

        Assert.Throws<InvalidOperationException>(() => Compile(new SqlServerEmitter(), builder));
    }

    // "Bakes" here means the runtime path needs no bind for a computed operand
    // -- unrelated to the source-generator baking pipeline Task 3 adds.
    [Fact]
    public void RowNumber_orders_by_a_computed_expression_without_a_bind()
    {
        var v = new SqlVocab();
        var sql = Compile(new SqlServerEmitter(), new SelectBuilder()
            .Select(Sql.As(Sql.RowNumber()
                .PartitionBy(v.VocabId)
                .OrderBy(TSql.RTrim(v.Code)), "rn"))
            .From(v.ToFrom()));

        Assert.Equal(
            "SELECT ROW_NUMBER() OVER (PARTITION BY [revdel0].[vocab_id] "
            + "ORDER BY rtrim([revdel0].[code])) AS [rn] FROM [dbo].[revdel0] AS [revdel0]",
            sql.Sql);
    }

    [Fact]
    public void RowNumber_emits_partition_and_order_on_postgres()
    {
        var v = new PgVocab();
        var sql = Compile(new PgEmitter(), new SelectBuilder()
            .Select(Sql.As(Sql.RowNumber()
                .PartitionBy(v.VocabId)
                .OrderBy(v.Code)
                .OrderByDesc(v.TypeId), "rn"))
            .From(v.ToFrom()));

        Assert.Equal(
            "SELECT ROW_NUMBER() OVER (PARTITION BY \"revdel0\".\"vocab_id\" "
            + "ORDER BY \"revdel0\".\"code\", \"revdel0\".\"type_id\" DESC) AS \"rn\" "
            + "FROM \"public\".\"revdel0\" AS \"revdel0\"",
            sql.Sql);
    }

    [Fact]
    public void RowNumber_needs_at_least_one_order_column_on_postgres()
    {
        var v = new PgVocab();
        var builder = new SelectBuilder()
            .Select(Sql.As(Sql.RowNumber().PartitionBy(v.VocabId), "rn"))
            .From(v.ToFrom());

        Assert.Throws<InvalidOperationException>(() => Compile(new PgEmitter(), builder));
    }
}
