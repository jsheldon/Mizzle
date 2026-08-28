using Microsoft.CodeAnalysis.CSharp;
using Mizzle.Compile;
using Mizzle.Fluent;
using Mizzle.Postgres;

namespace Mizzle.Generators.Tests;

// BakedSqlEmitter's contract is that it reproduces the runtime emitter's output
// for the statically-visible subset. Nothing enforced that, so the two could
// drift silently -- a baked query would run different SQL than the same query
// on the runtime path.
file sealed class Orders : PgTable<Orders>
{
    public Orders() : base("orders", "public") { }
    public PgColumn<Guid> OrderId { get; } = Uuid("order_id").PrimaryKey();
    public PgColumn<string> Status { get; } = Text("status").NotNull();
}

public sealed class BakedSqlParityTests
{
    private const string Tables = """
        using Mizzle.Postgres;

        namespace Demo;

        public sealed class Orders : PgTable<Orders>
        {
            public Orders() : base("orders", "public") { }
            public PgColumn<System.Guid> OrderId { get; } = Uuid("order_id").PrimaryKey();
            public PgColumn<string> Status { get; } = Text("status").NotNull();
        }
        """;

    private static string RuntimeSql(SelectBuilder builder)
    {
        var (canonical, values) = Parameterizer.Run(builder.Build());
        return new PgEmitter().Emit(canonical, values).Sql;
    }

    private static string BakedSql(string callSite)
    {
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(Tables, callSite));
        const string open = "ToListPrecompiledAsync(";
        const string close = ", global::Mizzle.Generated.Projections.";
        var start = generated.IndexOf(open, StringComparison.Ordinal);
        Assert.True(start >= 0, "no baked call found in generated output");
        start += open.Length;
        var end = generated.IndexOf(close, start, StringComparison.Ordinal);
        Assert.True(end > start, "no baked SQL literal found in generated output");
        return generated.Substring(start, end - start);
    }

    [Fact]
    public void Cte_query_bakes_the_same_sql_the_runtime_emits()
    {
        var o = new Orders();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(o.OrderId)
            .With(CteBuilder.Named("open", new SelectBuilder()
                .Select(o.OrderId)
                .From(o.ToFrom())
                .Where(o.Status.Eq("open"))
                .Build()))
            .From(o.ToFrom())
            .Where(o.OrderId.Eq(Guid.NewGuid())));

        var baked = BakedSql("""
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public record ParityRow(Guid OrderId);

            public static class ParityQ
            {
                public static async Task Run(PostgresDb db, Guid id)
                {
                    var o = new Orders();
                    var rows = await db.Select(o.OrderId)
                        .With(CteBuilder.Named("open", db.Select(o.OrderId).From(o).Where(o.Status.Eq("open")).Build()))
                        .From(o)
                        .Where(o.OrderId.Eq(id))
                        .ToListAsync<ParityRow>();
                }
            }
            """);

        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    [Fact]
    public void Grouped_aggregate_bakes_the_same_sql_the_runtime_emits()
    {
        var o = new Orders();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(o.Status, Sql.As(Sql.Count(), "N"), Sql.As(Sql.Min(o.OrderId), "First"))
            .From(o.ToFrom())
            .Where(o.Status.Eq("open"))
            .GroupBy(o.Status));

        var baked = BakedSql("""
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public record AggParityRow(string Status, long N, Guid? First);

            public static class AggParityQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var o = new Orders();
                    var rows = await db.Select(o.Status, Sql.As(Sql.Count(), "N"), Sql.As(Sql.Min(o.OrderId), "First"))
                        .From(o)
                        .Where(o.Status.Eq("open"))
                        .GroupBy(o.Status)
                        .ToListAsync<AggParityRow>();
                }
            }
            """);

        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    [Fact]
    public void Joined_query_bakes_the_same_sql_the_runtime_emits()
    {
        var o = new Orders();
        var other = new Orders().WithAlias("o2");
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(o.OrderId, other.Status.As("OtherStatus"))
            .From(o.ToFrom())
            .LeftJoin(other, Sql.Eq(o.OrderId, other.OrderId))
            .Where(o.Status.Eq("open")));

        var baked = BakedSql("""
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public record JoinParityRow(Guid OrderId, string? OtherStatus);

            public static class JoinParityQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var o = new Orders();
                    var other = new Orders().WithAlias("o2");
                    var rows = await db.Select(o.OrderId, other.Status.As("OtherStatus"))
                        .From(o)
                        .LeftJoin(other).On(o.OrderId.Eq(other.OrderId))
                        .Where(o.Status.Eq("open"))
                        .ToListAsync<JoinParityRow>();
                }
            }
            """);

        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }
}
