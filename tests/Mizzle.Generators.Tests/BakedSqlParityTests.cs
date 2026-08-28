using Microsoft.CodeAnalysis.CSharp;
using Mizzle.Compile;
using Mizzle.Fluent;
using Mizzle.Ir;
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
        // A single-shape query assigns its SQL literal directly; a conditional one
        // selects between variants, which these tests do not cover.
        const string open = "            var sql = ";
        var start = generated.IndexOf(open, StringComparison.Ordinal);
        Assert.True(start >= 0, "no baked SQL assignment found in generated output");
        start += open.Length;
        var lineEnd = generated.IndexOf('\n', start);
        Assert.True(lineEnd > start, "baked SQL assignment was not terminated");
        return generated.Substring(start, lineEnd - start).TrimEnd('\r').TrimEnd(';');
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

    [Fact]
    public void Having_bakes_and_matches_the_runtime_emitter()
    {
        var o = new Orders();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(o.Status, Sql.As(Sql.Count(), "N"))
            .From(o.ToFrom())
            .GroupBy(o.Status)
            .Having(Sql.Eq(Sql.Count(), new ValueExpr(2, typeof(int)))));

        var baked = BakedSql("""
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public record HavingRow(string Status, long N);

            public static class HavingQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var o = new Orders();
                    var rows = await db.Select(o.Status, Sql.As(Sql.Count(), "N"))
                        .From(o)
                        .GroupBy(o.Status)
                        .Having(Sql.Eq(Sql.Count(), Sql.Value(2)))
                        .ToListAsync<HavingRow>();
                }
            }
            """);

        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    [Fact]
    public void Literal_select_item_takes_its_slot_before_the_where_clause()
    {
        var o = new Orders();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(o.OrderId, Sql.As(Sql.Value(7), "Pri"))
            .From(o.ToFrom())
            .Where(o.Status.Eq("open")));

        var baked = BakedSql("""
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public record LiteralRow(Guid OrderId, int Pri);

            public static class LiteralQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var o = new Orders();
                    var rows = await db.Select(o.OrderId, Sql.As(Sql.Value(7), "Pri"))
                        .From(o)
                        .Where(o.Status.Eq("open"))
                        .ToListAsync<LiteralRow>();
                }
            }
            """);

        // Parameterizer order puts select items before where, so the literal is $1.
        Assert.Contains("$1 AS", baked, StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    [Fact]
    public void Union_all_bakes_and_matches_the_runtime_emitter()
    {
        var o = new Orders();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(o.OrderId)
            .From(o.ToFrom())
            .Where(o.Status.Eq("open"))
            .UnionAll(new SelectBuilder()
                .Select(o.OrderId)
                .From(o.ToFrom())
                .Where(o.Status.Eq("closed"))));

        var baked = BakedSql("""
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public record UnionRow(Guid OrderId);

            public static class UnionQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var o = new Orders();
                    var closed = db.Select(o.OrderId).From(o).Where(o.Status.Eq("closed"));
                    var rows = await db.Select(o.OrderId)
                        .From(o)
                        .Where(o.Status.Eq("open"))
                        .UnionAll(closed)
                        .ToListAsync<UnionRow>();
                }
            }
            """);

        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    [Theory]
    [InlineData("o.Status.Ne(\"open\")", "<> $1")]
    [InlineData("o.Status.Gt(\"a\")", "> $1")]
    [InlineData("o.Status.Gte(\"a\")", ">= $1")]
    [InlineData("o.Status.Lt(\"z\")", "< $1")]
    [InlineData("o.Status.Lte(\"z\")", "<= $1")]
    [InlineData("o.Status.Like(\"a%\")", "LIKE $1")]
    public void Comparison_operators_bake_and_match_the_runtime_emitter(string predicate, string expected)
    {
        var baked = BakedSql($$"""
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public record OpRow(Guid OrderId);

            public static class OpQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var o = new Orders();
                    var rows = await db.Select(o.OrderId).From(o).Where({{predicate}}).ToListAsync<OpRow>();
                }
            }
            """);

        Assert.Contains(expected, baked, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IsNull", "IS NULL")]
    [InlineData("IsNotNull", "IS NOT NULL")]
    public void Null_tests_bake_and_consume_no_bind_slot(string method, string expected)
    {
        var o = new Orders();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(o.OrderId)
            .From(o.ToFrom())
            .Where(method == "IsNull" ? o.Status.IsNull() : o.Status.IsNotNull())
            .Where(o.OrderId.Eq(Guid.NewGuid())));

        var baked = BakedSql($$"""
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public record NullRow(Guid OrderId);

            public static class NullQ
            {
                public static async Task Run(PostgresDb db, Guid id)
                {
                    var o = new Orders();
                    var rows = await db.Select(o.OrderId).From(o)
                        .Where(o.Status.{{method}}())
                        .Where(o.OrderId.Eq(id))
                        .ToListAsync<NullRow>();
                }
            }
            """);

        Assert.Contains(expected, baked, StringComparison.Ordinal);
        // The unary test takes no slot, so the following bind is still $1.
        Assert.Contains("\\\"order_id\\\" = $1", baked, StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }
}
