using Microsoft.CodeAnalysis;

namespace Mizzle.Generators.Tests;

// CteBuilder.Named<T>(name, body) declares T as a table type with one typed
// column per column the body's select list projects, so a CTE never needs a
// hand-declared, hand-synced table class.
public sealed class CteTableGeneratorTests
{
    private const string Tables = """
        using Mizzle.Postgres;

        namespace Demo;

        public sealed class Orders : PgTable<Orders>
        {
            public Orders() : base("orders", "public") { }
            public PgColumn<System.Guid> OrderId { get; } = Uuid("order_id").PrimaryKey();
            public PgColumn<string> Ndc { get; } = Text("ndc").NotNull();
            public PgColumn<System.DateTime> EffectiveDate { get; } = Timestamp("effective_date").NotNull();
        }
        """;

    private const string CallSite = """
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.Postgres;

        namespace Demo;

        public sealed record BestNdc(string Ndc);

        public static class RankedQ
        {
            public static async Task Run(PostgresDb db)
            {
                var o = new Orders();
                var body = db.Select(
                        o.Ndc,
                        Sql.As(Sql.RowNumber().PartitionBy(o.Ndc).OrderByDesc(o.EffectiveDate), "rn"))
                    .From(o)
                    .Build();

                var cte = CteBuilder.Named<Ranked>("ranked", body);
                var ranked = new Ranked();
                var rows = await db.Select(ranked.Ndc)
                    .With(cte)
                    .From(ranked)
                    .Where(ranked.rn.Eq(1))
                    .ToListAsync<BestNdc>();
            }
        }
        """;

    [Fact]
    public void Declares_a_table_type_with_one_column_per_projected_column()
    {
        var result = GeneratorTestHost.Run(Tables, CallSite);
        var generated = GeneratorTestHost.Generated(result);

        // MIZ014 on the outer ToListAsync<BestNdc>() is an expected artifact of
        // generating and consuming a CTE table in one compilation pass: Ranked
        // does not exist yet for that call site's OWN resolution within this same
        // pass, only for a later build once it is real, ordinary source. What
        // matters here is that the CTE table itself generates cleanly.
        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "MIZ015" or "MIZ016");

        Assert.Contains("public sealed class Ranked", generated, StringComparison.Ordinal);
        Assert.Contains(": global::Mizzle.Postgres.PgTable<Ranked>", generated, StringComparison.Ordinal);
        Assert.Contains("""base("ranked")""", generated, StringComparison.Ordinal);
        // Ndc is an unaliased passthrough of Orders.Ndc, whose db column name is
        // "ndc" (lowercase) -- that must be the factory argument, not the C#
        // property name "Ndc", or the generated column would reference a column
        // the CTE's result set does not actually have.
        Assert.Contains("""global::Mizzle.Postgres.PgColumn<string> Ndc { get; } = Text("ndc").NotNull();""", generated, StringComparison.Ordinal);
        // rn is a computed column (ROW_NUMBER), which has no db column at all --
        // its own alias is both the C# property name and the factory argument.
        Assert.Contains("""global::Mizzle.Postgres.PgColumn<long> rn { get; } = BigInt("rn").NotNull();""", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_table_compiles_and_its_columns_are_usable_in_where_and_from()
    {
        var (_, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, CallSite);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void An_already_existing_type_argument_generates_nothing()
    {
        const string existing = """
            using Mizzle.Postgres;

            namespace Demo;

            public sealed class AlreadyThere : PgTable<AlreadyThere>
            {
                public AlreadyThere() : base("ranked") { }
                public PgColumn<string> Ndc { get; } = Text("ndc").NotNull();
            }
            """;
        var callSite = CallSite.Replace("Ranked", "AlreadyThere");

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(Tables, existing, callSite));

        Assert.DoesNotContain("Mizzle.CteTables", generated, StringComparison.Ordinal);
    }
}
