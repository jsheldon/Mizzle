using Mizzle.Compile;
using Mizzle.Fluent;
using Mizzle.Ir;
using Mizzle.Postgres;

namespace Mizzle.Tests;

file sealed class Orders : PgTable<Orders>
{
    public Orders() : base("orders", "public") { }
    public PgColumn<Guid> OrderId { get; } = Uuid("order_id").NotNull();
    public PgColumn<Guid> CustomerId { get; } = Uuid("customer_id").NotNull();
    public PgColumn<decimal> Total { get; } = Numeric("total").NotNull();
    public PgColumn<string> Status { get; } = Text("status").NotNull();
}

public sealed class AggregationTests
{
    private static string EmitPg(SelectQuery q)
    {
        var (canonical, values) = Parameterizer.Run(q);
        return new PgEmitter().Emit(canonical, values).Sql;
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
}
