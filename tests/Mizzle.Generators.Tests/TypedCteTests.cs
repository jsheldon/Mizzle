using Microsoft.CodeAnalysis;

namespace Mizzle.Generators.Tests;

// A CTE participates in the type system by declaring its shape as a table whose
// name is the CTE name. Columns are typed, As(...) works, the query bakes, and
// the projection diagnostics apply -- no separate CTE-table concept needed.
public sealed class TypedCteTests
{
    private const string Tables = """
        using System;
        using Mizzle.Postgres;

        namespace Demo;

        public sealed class Orders : PgTable<Orders>
        {
            public Orders() : base("orders", "public") { }
            public PgColumn<Guid> OrderId { get; } = Uuid("order_id").NotNull();
            public PgColumn<string> Ndc { get; } = Text("ndc").NotNull();
        }

        // Shape of the CTE body. The table name is the CTE name; no schema.
        public sealed class RxNorm : PgTable<RxNorm>
        {
            public RxNorm() : base("rxnorm") { }
            public PgColumn<string> Ndc { get; } = Text("ndc").NotNull();
            public PgColumn<string> Code { get; } = Text("code").NotNull();
        }
        """;

    private const string CallSite = """
        using System;
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.Postgres;

        namespace Demo;

        public record OrderCode(Guid OrderId, string? Code);

        public static class CteQ
        {
            public static async Task Run(PostgresDb db)
            {
                var o = new Orders();
                var rx = new RxNorm();
                var rows = await db.Select(o.OrderId, rx.Code.As("Code"))
                    .With(CteBuilder.Named("rxnorm", db.Select(o.Ndc).From(o).Build()))
                    .From(o)
                    .LeftJoin(rx).On(o.Ndc.Eq(rx.Ndc))
                    .ToListAsync<OrderCode>();
            }
        }
        """;

    [Fact]
    public void A_cte_declared_as_a_table_joins_with_typed_columns_and_bakes()
    {
        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, CallSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains("WITH \\\"rxnorm\\\" AS (SELECT", generated, StringComparison.Ordinal);
        // Referenced by name, with no schema qualifier.
        Assert.Contains("LEFT JOIN \\\"rxnorm\\\" AS \\\"rxnorm\\\"", generated, StringComparison.Ordinal);
        Assert.Contains("\\\"rxnorm\\\".\\\"code\\\" AS \\\"Code\\\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Left_joining_a_cte_still_demotes_its_columns_to_nullable()
    {
        // Code is NotNull() on the CTE table, but the LEFT JOIN makes it nullable --
        // binding it to a non-nullable member is MIZ005, as for any other table.
        var callSite = CallSite.Replace("string? Code", "string Code");
        var result = GeneratorTestHost.Run(Tables, callSite);
        Assert.Contains(result.Diagnostics, d => d.Id == "MIZ005");
    }
}
