using Microsoft.CodeAnalysis.CSharp;
using Mizzle.Compile;
using Mizzle.Fluent;
using Mizzle.SqlServer;

namespace Mizzle.Generators.Tests;

// Sql.Avg(Column<int>) and Sql.Avg<T>(Expr) are different overloads that behave
// differently on SQL Server (the first casts the argument to decimal to avoid
// integer-division truncation; the second applies no cast at all, trusting the
// caller's stated type). The walker has to tell them apart by which overload
// actually resolved, not by whether the argument happens to look like a column,
// or baked and runtime SQL can silently diverge.
file sealed class Orders : SqlTable<Orders>
{
    public Orders() : base("orders", "dbo") { }
    public SqlColumn<int> Quantity { get; } = Int("quantity").NotNull();
}

public sealed class AggregatePromotionBakeTests
{
    private const string Tables = """
        using Mizzle.SqlServer;

        namespace Demo;

        public sealed class Orders : SqlTable<Orders>
        {
            public Orders() : base("orders", "dbo") { }
            public SqlColumn<int> Quantity { get; } = Int("quantity").NotNull();
        }
        """;

    private static string RuntimeSql(SelectBuilder builder)
    {
        var (canonical, values) = Parameterizer.Run(builder.Build());
        return new SqlServerEmitter().Emit(canonical, values).Sql;
    }

    private static string BakedSql(string callSite)
    {
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(Tables, callSite));
        const string open = "            var sql = ";
        var start = generated.IndexOf(open, StringComparison.Ordinal);
        Assert.True(start >= 0, "no baked SQL assignment found in generated output:\n" + generated);
        start += open.Length;
        var lineEnd = generated.IndexOf('\n', start);
        Assert.True(lineEnd > start, "baked SQL assignment was not terminated");
        return generated.Substring(start, lineEnd - start).TrimEnd('\r').TrimEnd(';');
    }

    [Fact]
    public void The_column_typed_overload_casts_and_bakes_the_same_sql_the_runtime_emits()
    {
        var o = new Orders();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(Sql.Avg(o.Quantity).As("Avg"))
            .From(o.ToFrom()));

        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            public record AvgRow(decimal? Avg);

            public static class ColumnAvgQ
            {
                public static async Task Run(SqlDb db)
                {
                    var o = new Orders();
                    var rows = await db.Select(Sql.Avg(o.Quantity).As("Avg"))
                        .From(o)
                        .ToListAsync<AvgRow>();
                }
            }
            """);

        Assert.Contains("avg(CAST([orders].[quantity] AS DECIMAL(38, 6)))", baked, StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    [Fact]
    public void The_expr_typed_escape_hatch_applies_no_cast_even_over_a_plain_column_and_bakes_the_same_sql_the_runtime_emits()
    {
        // An explicit type argument selects the Expr-typed escape hatch (Avg<T>(Expr)),
        // never the Column<int>-typed fixed overload, even though orders.Quantity is a
        // plain column here -- the runtime applies no promotion/cast for this overload,
        // so this stays plain integer averaging (with its truncation) on SQL Server.
        var o = new Orders();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(Sql.Avg<decimal>(o.Quantity).As("Avg"))
            .From(o.ToFrom()));

        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            public record AvgRow(decimal? Avg);

            public static class ExprAvgQ
            {
                public static async Task Run(SqlDb db)
                {
                    var o = new Orders();
                    var rows = await db.Select(Sql.Avg<decimal>(o.Quantity).As("Avg"))
                        .From(o)
                        .ToListAsync<AvgRow>();
                }
            }
            """);

        Assert.Contains("avg([orders].[quantity])", baked, StringComparison.Ordinal);
        Assert.DoesNotContain("CAST", baked, StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }
}
