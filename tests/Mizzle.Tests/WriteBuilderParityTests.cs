using Mizzle.Compile;
using Mizzle.Fluent;
using Mizzle.Ir;
using Mizzle.Postgres;

namespace Mizzle.Tests;

file sealed class Widgets : PgTable<Widgets>
{
    public Widgets() : base("widgets") { }
    public PgColumn<Guid> WidgetId { get; } = Uuid("widget_id").NotNull();
    public PgColumn<string> Label { get; } = Text("label").NotNull();
    public PgColumn<int> Qty { get; } = Integer("qty").NotNull();
}

public sealed class WriteBuilderParityTests
{
    private static string EmitPg(Query q)
    {
        var (canonical, values) = Parameterizer.Run(q);
        return new PgEmitter().Emit(canonical, values).Sql;
    }

    [Fact]
    public void Update_returning_carries_projection_aliases()
    {
        var w = new Widgets();
        var sql = EmitPg(new UpdateBuilder(w)
            .Set(w.Qty, 5)
            .Where(w.WidgetId, Guid.NewGuid())
            .Returning(w.WidgetId.As("Id"), w.Label)
            .Build());

        Assert.Contains("RETURNING \"widgets\".\"widget_id\" AS \"Id\", \"widgets\".\"label\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_returning_carries_projection_aliases()
    {
        var w = new Widgets();
        var sql = EmitPg(new DeleteBuilder(w)
            .Where(w.WidgetId, Guid.NewGuid())
            .Returning(w.WidgetId.As("Id"))
            .Build());

        Assert.Contains("RETURNING \"widgets\".\"widget_id\" AS \"Id\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_emits_a_cte_prefix()
    {
        var w = new Widgets();
        var source = new SelectBuilder().Select(w.WidgetId).From(w.ToFrom()).Build();
        var sql = EmitPg(new UpdateBuilder(w)
            .With(CteBuilder.Named("stale", source))
            .Set(w.Qty, 0)
            .Build());

        Assert.StartsWith("WITH \"stale\" AS (SELECT", sql, StringComparison.Ordinal);
        Assert.Contains("UPDATE", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_emits_a_cte_prefix()
    {
        var w = new Widgets();
        var source = new SelectBuilder().Select(w.WidgetId).From(w.ToFrom()).Build();
        var sql = EmitPg(new DeleteBuilder(w)
            .With(CteBuilder.Named("stale", source))
            .Build());

        Assert.StartsWith("WITH \"stale\" AS (SELECT", sql, StringComparison.Ordinal);
        Assert.Contains("DELETE", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Insert_emits_a_cte_prefix()
    {
        var w = new Widgets();
        var source = new SelectBuilder().Select(w.WidgetId).From(w.ToFrom()).Build();
        var sql = EmitPg(new InsertBuilder(w)
            .With(CteBuilder.Named("seed", source))
            .Value(w.Qty, 1)
            .Build());

        Assert.StartsWith("WITH \"seed\" AS (SELECT", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Recursive_cte_is_marked_on_writes()
    {
        var w = new Widgets();
        var source = new SelectBuilder().Select(w.WidgetId).From(w.ToFrom()).Build();
        var sql = EmitPg(new DeleteBuilder(w)
            .WithRecursive(CteBuilder.Named("tree", source))
            .Build());

        Assert.StartsWith("WITH RECURSIVE \"tree\" AS (", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Typed_update_projection_without_returning_is_rejected()
    {
        var w = new Widgets();
        var builder = new UpdateBuilder(w).Set(w.Qty, 1);
        var error = Assert.Throws<InvalidOperationException>(() => builder.ToListAsync<Widgets>().GetAwaiter().GetResult());
        Assert.Contains("Returning", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Typed_delete_projection_without_returning_is_rejected()
    {
        var w = new Widgets();
        var builder = new DeleteBuilder(w);
        var error = Assert.Throws<InvalidOperationException>(() => builder.ToListAsync<Widgets>().GetAwaiter().GetResult());
        Assert.Contains("Returning", error.Message, StringComparison.Ordinal);
    }
}
