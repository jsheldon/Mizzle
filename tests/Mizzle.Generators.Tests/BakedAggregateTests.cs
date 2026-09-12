using Microsoft.CodeAnalysis;

namespace Mizzle.Generators.Tests;

public sealed class BakedAggregateTests
{
    private const string Tables = """
        using System;
        using Mizzle.Postgres;

        namespace Demo;

        public sealed class Orders : PgTable<Orders>
        {
            public Orders() : base("orders", "public") { }
            public PgColumn<Guid> OrderId { get; } = Uuid("order_id").NotNull();
            public PgColumn<Guid> CustomerId { get; } = Uuid("customer_id").NotNull();
            public PgColumn<decimal> Total { get; } = Numeric("total").NotNull();
            public PgColumn<string> Status { get; } = Text("status").NotNull();
        }
        """;

    [Fact]
    public void Grouped_aggregate_query_is_baked()
    {
        const string callSite = """
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public record CustomerTotals(Guid CustomerId, long Orders, decimal? Revenue);

            public static class AggQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var o = new Orders();
                    var rows = await db.Select(
                            o.CustomerId,
                            Sql.As(Sql.Count(), "Orders"),
                            Sql.As(Sql.Sum(o.Total), "Revenue"))
                        .From(o)
                        .GroupBy(o.CustomerId)
                        .ToListAsync<CustomerTotals>();
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, callSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains(
            "SELECT \\\"orders\\\".\\\"customer_id\\\", count(*) AS \\\"Orders\\\", sum(\\\"orders\\\".\\\"total\\\") AS \\\"Revenue\\\"",
            generated, StringComparison.Ordinal);
        Assert.Contains("GROUP BY \\\"orders\\\".\\\"customer_id\\\"", generated, StringComparison.Ordinal);

        // The aggregate reads as the member's own type, not a guessed one.
        Assert.Contains("Orders: r.GetFieldValue<long>(1)", generated, StringComparison.Ordinal);
        // A nullable member keeps its null check -- MIN/SUM return NULL on empty groups.
        Assert.Contains("Revenue: r.IsDBNull(2) ? (decimal?)null : r.GetFieldValue<decimal>(2)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Nameof_aggregate_alias_stays_on_the_baked_path()
    {
        const string callSite = """
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public record CustomerTotals(Guid CustomerId, long Orders);

            public static class NameofAggQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var o = new Orders();
                    var rows = await db.Select(
                            o.CustomerId,
                            Sql.As(Sql.Count(), nameof(CustomerTotals.Orders)))
                        .From(o)
                        .GroupBy(o.CustomerId)
                        .ToListAsync<CustomerTotals>();
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, callSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains(
            "SELECT \\\"orders\\\".\\\"customer_id\\\", count(*) AS \\\"Orders\\\"",
            generated, StringComparison.Ordinal);
        Assert.Contains("Orders: r.GetFieldValue<long>(1)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Aggregate_result_type_follows_the_member_across_dialects()
    {
        // count(*) is bigint on Postgres and int on SQL Server; declaring int here
        // must produce GetFieldValue<int>, not a hard-coded long.
        const string callSite = """
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public record NarrowCount(Guid CustomerId, int Orders);

            public static class NarrowQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var o = new Orders();
                    var rows = await db.Select(o.CustomerId, Sql.As(Sql.Count(), "Orders"))
                        .From(o)
                        .GroupBy(o.CustomerId)
                        .ToListAsync<NarrowCount>();
                }
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(Tables, callSite));
        Assert.Contains("Orders: r.GetFieldValue<int>(1)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Unaliased_aggregate_falls_back_to_runtime()
    {
        const string callSite = """
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public record BareCount(long Count);

            public static class BareQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var o = new Orders();
                    var rows = await db.Select(Sql.Count()).From(o).ToListAsync<BareCount>();
                }
            }
            """;

        // Nothing names the aggregate, so there is no member to bind it to; that now
        // surfaces as MIZ014 instead of compiling silently into a runtime throw.
        var result = GeneratorTestHost.Run(Tables, callSite);
        Assert.Contains(result.Diagnostics, d => d.Id == "MIZ014" && d.Severity == DiagnosticSeverity.Warning);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("BareCountIntoMapper", GeneratorTestHost.Generated(result), StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_over_a_computed_operand_bakes_in_strict_mode()
    {
        // Sql.Sum<decimal>(Sql.Case(...)) compiles (the Expr-typed escape hatch),
        // but until now the walker's operand resolution only accepted a plain
        // column, so this shape could never actually bake -- it fell back to the
        // runtime path even under MizzleQueryMode=Strict, which should fail
        // closed (MIZ002) rather than silently degrade.
        const string callSite = """
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public record OpenTotal(Guid CustomerId, decimal? Revenue);

            public static class ComputedAggQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var o = new Orders();
                    var rows = await db.Select(
                            o.CustomerId,
                            Sql.As(
                                Sql.Sum<decimal>(Sql.Case(Sql.When(o.Status.Eq("open"), o.Total)).Else(0m)),
                                "Revenue"))
                        .From(o)
                        .GroupBy(o.CustomerId)
                        .ToListAsync<OpenTotal>();
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, callSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains("sum(CASE WHEN", generated, StringComparison.Ordinal);
        Assert.Contains("AS \\\"Revenue\\\"", generated, StringComparison.Ordinal);
        // The caller's explicit <decimal> drives the reader, not a guessed "object".
        Assert.Contains("GetFieldValue<decimal>", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_over_a_computed_operand_bakes_in_having()
    {
        const string callSite = """
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public record OpenTotal(Guid CustomerId, decimal? Revenue);

            public static class ComputedHavingQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var o = new Orders();
                    var rows = await db.Select(
                            o.CustomerId,
                            Sql.As(
                                Sql.Sum<decimal>(Sql.Case(Sql.When(o.Status.Eq("open"), o.Total)).Else(0m)),
                                "Revenue"))
                        .From(o)
                        .GroupBy(o.CustomerId)
                        .Having(Sql.Sum<decimal>(Sql.Case(Sql.When(o.Status.Eq("open"), o.Total)).Else(0m)).Gt(100m))
                        .ToListAsync<OpenTotal>();
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, callSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains("HAVING sum(CASE WHEN", generated, StringComparison.Ordinal);
    }
}
