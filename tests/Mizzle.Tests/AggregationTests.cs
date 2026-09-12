using Mizzle.Compile;
using Mizzle.Fluent;
using Mizzle.Ir;
using Mizzle.Postgres;
using Mizzle.SqlServer;

namespace Mizzle.Tests;

file sealed class Orders : PgTable<Orders>
{
    public Orders() : base("orders", "public") { }
    public PgColumn<Guid> OrderId { get; } = Uuid("order_id").NotNull();
    public PgColumn<Guid> CustomerId { get; } = Uuid("customer_id").NotNull();
    public PgColumn<decimal> Total { get; } = Numeric("total").NotNull();
    public PgColumn<string> Status { get; } = Text("status").NotNull();
    public PgColumn<int> Quantity { get; } = Integer("quantity").NotNull();
    public PgColumn<float> Weight { get; } = Real("weight").NotNull();
    public PgColumn<long> ViewCount { get; } = BigInt("view_count").NotNull();
}

public sealed class AggregationTests
{
    private static string EmitPg(SelectQuery q)
    {
        var (canonical, values) = Parameterizer.Run(q);
        return new PgEmitter().Emit(canonical, values).Sql;
    }

    private static string EmitSqlServer(SelectQuery q)
    {
        var (canonical, values) = Parameterizer.Run(q);
        return new SqlServerEmitter().Emit(canonical, values).Sql;
    }

    [Fact]
    public void Aggregates_and_columns_mix_in_one_select_list()
    {
        var o = new Orders();
        var sql = EmitPg(new SelectBuilder()
            .Select(o.CustomerId, Sql.As(Sql.Count(), "Orders"), Sql.As(Sql.Sum(o.Total), "Revenue"))
            .From(o.ToFrom())
            .GroupBy(o.CustomerId)
            .Build());

        Assert.Equal(
            "SELECT \"orders\".\"customer_id\", count(*) AS \"Orders\", sum(\"orders\".\"total\") AS \"Revenue\" "
            + "FROM \"public\".\"orders\" AS \"orders\" GROUP BY \"orders\".\"customer_id\"",
            sql);
    }

    [Fact]
    public void Having_filters_grouped_rows_and_ands_when_repeated()
    {
        var o = new Orders();
        var sql = EmitPg(new SelectBuilder()
            .Select(o.CustomerId, Sql.As(Sql.Count(), "Orders"))
            .From(o.ToFrom())
            .GroupBy(o.CustomerId)
            .Having(Sql.Eq(Sql.Count(), new ValueExpr(2, typeof(int))))
            .Having(Sql.Eq(Sql.Min(o.Total), new ValueExpr(10m, typeof(decimal))))
            .Build());

        Assert.Contains("GROUP BY \"orders\".\"customer_id\" HAVING", sql, StringComparison.Ordinal);
        Assert.Contains("count(*) = $1", sql, StringComparison.Ordinal);
        Assert.Contains("min(\"orders\".\"total\") = $2", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Min_max_avg_all_emit()
    {
        var o = new Orders();
        var sql = EmitPg(new SelectBuilder()
            .Select(Sql.As(Sql.Min(o.Total), "Lo"), Sql.As(Sql.Max(o.Total), "Hi"), Sql.As(Sql.Avg(o.Total), "Mean"))
            .From(o.ToFrom())
            .Build());

        Assert.Contains("min(\"orders\".\"total\") AS \"Lo\"", sql, StringComparison.Ordinal);
        Assert.Contains("max(\"orders\".\"total\") AS \"Hi\"", sql, StringComparison.Ordinal);
        Assert.Contains("avg(\"orders\".\"total\") AS \"Mean\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Literals_can_be_projected_as_a_constant_column()
    {
        var o = new Orders();
        var sql = EmitPg(new SelectBuilder()
            .Select(o.OrderId, Sql.As(Sql.Value(0), "pri"))
            .From(o.ToFrom())
            .Build());

        Assert.Contains("$1 AS \"pri\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Union_all_appends_a_branch()
    {
        var o = new Orders();
        var open = new SelectBuilder().Select(o.OrderId).From(o.ToFrom()).Where(o.Status.Eq("open"));
        var closed = new SelectBuilder().Select(o.OrderId).From(o.ToFrom()).Where(o.Status.Eq("closed"));

        var sql = EmitPg(open.UnionAll(closed).Build());

        Assert.Contains(" UNION ALL ", sql, StringComparison.Ordinal);
        Assert.Contains("\"status\" = $1", sql, StringComparison.Ordinal);
        Assert.Contains("\"status\" = $2", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void A_column_keeps_its_projection_alias_in_an_expression_select_list()
    {
        var o = new Orders();
        var sql = EmitPg(new SelectBuilder()
            .Select(o.OrderId.As("Id"), Sql.As(Sql.Count(), "N"))
            .From(o.ToFrom())
            .Build());

        Assert.Contains("\"orders\".\"order_id\" AS \"Id\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_aliases_itself_fluently()
    {
        var o = new Orders();
        var sql = EmitPg(new SelectBuilder()
            .Select(o.CustomerId, Sql.Count().As("Orders"), Sql.Sum(o.Total).As("Revenue"))
            .From(o.ToFrom())
            .GroupBy(o.CustomerId)
            .Build());

        Assert.Equal(
            "SELECT \"orders\".\"customer_id\", count(*) AS \"Orders\", sum(\"orders\".\"total\") AS \"Revenue\" "
            + "FROM \"public\".\"orders\" AS \"orders\" GROUP BY \"orders\".\"customer_id\"",
            sql);
    }

    [Fact]
    public void A_computed_expression_aliases_itself_fluently()
    {
        var o = new Orders();
        var sql = EmitPg(new SelectBuilder()
            .Select(o.CustomerId, Sql.RowNumber().PartitionBy(o.CustomerId).OrderByDesc(o.Total).As("Rk"))
            .From(o.ToFrom())
            .Build());

        Assert.Contains(") AS \"Rk\"", sql, StringComparison.Ordinal);
        Assert.Contains("ROW_NUMBER() OVER (PARTITION BY", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_compares_with_a_typed_value_in_having()
    {
        var o = new Orders();
        var sql = EmitPg(new SelectBuilder()
            .Select(o.CustomerId, Sql.Count().As("Orders"))
            .From(o.ToFrom())
            .GroupBy(o.CustomerId)
            .Having(Sql.Count().Gt(2L))
            .Having(Sql.Min(o.Total).Gte(10m))
            .Build());

        Assert.Contains("HAVING", sql, StringComparison.Ordinal);
        Assert.Contains("count(*) > $1", sql, StringComparison.Ordinal);
        Assert.Contains("min(\"orders\".\"total\") >= $2", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Count_uses_count_big_on_sql_server_so_the_long_typed_result_is_honest()
    {
        var o = new Orders();
        var query = new SelectBuilder()
            .Select(o.CustomerId, Sql.Count().As("Orders"))
            .From(o.ToFrom())
            .GroupBy(o.CustomerId)
            .Build();

        // Postgres's COUNT is already bigint; SQL Server's plain COUNT is int, so
        // it must render COUNT_BIG for Sql.Count()'s long-typed result to be
        // correct on both dialects, not just the one the test happens to target.
        Assert.Contains("count(*) AS \"Orders\"", EmitPg(query), StringComparison.Ordinal);
        Assert.Contains("count_big(*) AS [Orders]", EmitSqlServer(query), StringComparison.Ordinal);
    }

    [Fact]
    public void Sum_of_an_int_column_widens_to_long_and_casts_on_sql_server()
    {
        var o = new Orders();
        var query = new SelectBuilder()
            .Select(o.CustomerId, Sql.Sum(o.Quantity).As("Total"))
            .From(o.ToFrom())
            .GroupBy(o.CustomerId)
            .Build();

        // Postgres's SUM(integer) is already bigint; SQL Server's stays int and can
        // overflow while accumulating, so the argument -- not just the final
        // result -- must be cast: SUM(CAST(col AS BIGINT)), not
        // CAST(SUM(col) AS BIGINT), which casts only after an int-width overflow
        // could already have happened.
        Assert.Contains("sum(\"orders\".\"quantity\") AS \"Total\"", EmitPg(query), StringComparison.Ordinal);
        Assert.Contains("sum(CAST([orders].[quantity] AS BIGINT)) AS [Total]", EmitSqlServer(query), StringComparison.Ordinal);
    }

    [Fact]
    public void Sum_of_a_long_column_widens_to_decimal_and_casts_on_sql_server()
    {
        var o = new Orders();
        var query = new SelectBuilder()
            .Select(o.CustomerId, Sql.Sum(o.ViewCount).As("TotalViews"))
            .From(o.ToFrom())
            .GroupBy(o.CustomerId)
            .Build();

        // Postgres's SUM(bigint) widens to numeric (bigint sums can exceed bigint
        // range); SQL Server's stays bigint and can silently overflow. Casting the
        // argument to decimal before summing -- not the final result -- avoids
        // that overflow instead of just relabeling an already-wrapped total.
        Assert.Contains("sum(\"orders\".\"view_count\") AS \"TotalViews\"", EmitPg(query), StringComparison.Ordinal);
        Assert.Contains("sum(CAST([orders].[view_count] AS DECIMAL(38, 0))) AS [TotalViews]", EmitSqlServer(query), StringComparison.Ordinal);
    }

    [Fact]
    public void Avg_of_an_int_column_widens_to_decimal_and_avoids_truncation_on_sql_server()
    {
        var o = new Orders();
        var query = new SelectBuilder()
            .Select(o.CustomerId, Sql.Avg(o.Quantity).As("Avg"))
            .From(o.ToFrom())
            .GroupBy(o.CustomerId)
            .Build();

        // Postgres's AVG(integer) already returns numeric (no truncation). SQL
        // Server's AVG(int) performs integer division and truncates the result
        // unless the argument is cast to decimal first -- a real value bug, not
        // just a type-label mismatch, if left unfixed.
        Assert.Contains("avg(\"orders\".\"quantity\") AS \"Avg\"", EmitPg(query), StringComparison.Ordinal);
        Assert.Contains("avg(CAST([orders].[quantity] AS DECIMAL(38, 6))) AS [Avg]", EmitSqlServer(query), StringComparison.Ordinal);
    }

    [Fact]
    public void Sum_of_a_real_column_widens_to_double_and_casts_on_postgres()
    {
        var o = new Orders();
        var query = new SelectBuilder()
            .Select(o.CustomerId, Sql.Sum(o.Weight).As("TotalWeight"))
            .From(o.ToFrom())
            .GroupBy(o.CustomerId)
            .Build();

        // Postgres's SUM(real) stays real (single precision); SQL Server's
        // SUM(real) already widens to float (double precision) natively -- here
        // it's Postgres, not SQL Server, that needs the cast.
        Assert.Contains("CAST(sum(\"orders\".\"weight\") AS DOUBLE PRECISION) AS \"TotalWeight\"", EmitPg(query), StringComparison.Ordinal);
        Assert.Contains("sum([orders].[weight]) AS [TotalWeight]", EmitSqlServer(query), StringComparison.Ordinal);
    }

    [Fact]
    public void A_computed_operand_still_compiles_with_an_explicit_result_type()
    {
        var o = new Orders();
        var sql = EmitPg(new SelectBuilder()
            .Select(Sql.Sum<decimal>(Sql.Case(Sql.When(o.Status.Eq("open"), o.Total)).Else(0m)).As("OpenTotal"))
            .From(o.ToFrom())
            .Build());

        Assert.Contains("sum(CASE WHEN", sql, StringComparison.Ordinal);
        Assert.Contains("AS \"OpenTotal\"", sql, StringComparison.Ordinal);
    }
}
