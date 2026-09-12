using System.Collections.Immutable;
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

    [Fact]
    public void AsCte_sugar_declares_the_same_table_type_as_CteBuilder_Named()
    {
        const string callSite = """
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
                    var cte = db.Select(
                            o.Ndc,
                            Sql.RowNumber().PartitionBy(o.Ndc).OrderByDesc(o.EffectiveDate).As("rn"))
                        .From(o)
                        .AsCte<Ranked>("ranked");
                    var ranked = new Ranked();
                    var rows = await db.Select(ranked.Ndc)
                        .With(cte)
                        .From(ranked)
                        .Where(ranked.rn.Eq(1))
                        .ToListAsync<BestNdc>();
                }
            }
            """;

        var result = GeneratorTestHost.Run(Tables, callSite);
        var generated = GeneratorTestHost.Generated(result);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "MIZ015" or "MIZ016");
        Assert.Contains("public sealed class Ranked", generated, StringComparison.Ordinal);
        Assert.Contains(": global::Mizzle.Postgres.PgTable<Ranked>", generated, StringComparison.Ordinal);
        Assert.Contains("""global::Mizzle.Postgres.PgColumn<long> rn { get; } = BigInt("rn").NotNull();""", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void The_consuming_query_bakes_through_a_local_holding_AsCte()
    {
        // Ranked as real, already-existing source -- the second-pass state once a
        // prior build has generated it (see the "one compilation pass" note on
        // Declares_a_table_type_with_one_column_per_projected_column). This is
        // what actually proves .With(cte) resolves a local initialized by AsCte
        // well enough to bake the OUTER query, not just that AsCte's own CTE
        // table generates -- ResolveCte previously only recognized Named(...).
        const string ranked = """
            using Mizzle.Postgres;

            namespace Demo;

            public sealed class Ranked : PgTable<Ranked>
            {
                public Ranked() : base("ranked") { }
                public PgColumn<string> Ndc { get; } = Text("ndc").NotNull();
                public PgColumn<long> Rn { get; } = BigInt("rn").NotNull();
            }
            """;

        const string callSite = """
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
                    var cte = db.Select(
                            o.Ndc,
                            Sql.RowNumber().PartitionBy(o.Ndc).OrderByDesc(o.EffectiveDate).As("Rn"))
                        .From(o)
                        .AsCte<Ranked>("ranked");
                    var ranked = new Ranked();
                    var rows = await db.Select(ranked.Ndc)
                        .With(cte)
                        .From(ranked)
                        .Where(ranked.Rn.Eq(1L))
                        .ToListAsync<BestNdc>();
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, ranked, callSite);
        var generated = GeneratorTestHost.Generated(result);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        // MIZ002 is the Strict-mode failure this bug produced: with ResolveCte
        // unable to see through the AsCte-initialized local, the outer query's
        // own terminator could not bake at all.
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "MIZ002");
        Assert.Contains("WITH \\\"ranked\\\" AS (SELECT", generated, StringComparison.Ordinal);
        Assert.Contains("FROM \\\"ranked\\\" AS \\\"ranked\\\"", generated, StringComparison.Ordinal);
    }

    private const string RankedTable = """
        using Mizzle.Postgres;

        namespace Demo;

        public sealed class Ranked : PgTable<Ranked>
        {
            public Ranked() : base("ranked") { }
            public PgColumn<string> Ndc { get; } = Text("ndc").NotNull();
        }
        """;

    // Asserts the sources compile cleanly (via the normal generator pipeline, which
    // doesn't depend on MizzleQueryMode) before trusting a Strict-mode diagnostic
    // from Analyze -- otherwise a parse/bind error elsewhere in the sources could
    // produce a MIZ002-shaped false positive (or mask a false negative) that has
    // nothing to do with the condition the test means to exercise.
    private static ImmutableArray<Diagnostic> AnalyzeValidSource(string[] sources, string queryMode)
    {
        var (_, compileDiagnostics) = GeneratorTestHost.RunAndCompile(sources);
        Assert.DoesNotContain(compileDiagnostics, d => d.Severity == DiagnosticSeverity.Error);

        return GeneratorTestHost.Analyze(sources, queryMode);
    }

    [Fact]
    public void A_reassigned_cte_local_fails_closed_instead_of_baking_the_stale_definition()
    {
        // If ResolveDeclaredExpression trusted only the initializer, the baked
        // query would use the FIRST definition of `cte` while runtime execution --
        // which reads the local's current value at the .With(cte) call site --
        // uses the second, so the interceptor would run different SQL than the
        // query it was generated from. This must fail closed (MIZ002 under
        // Strict mode), never silently bake the stale definition.
        const string callSite = """
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public sealed record NdcRow(string Ndc);

            public static class ReassignedCteQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var o = new Orders();
                    var cte = db.Select(o.Ndc).From(o).Where(o.Ndc.Eq("A")).AsCte<Ranked>("ranked");
                    cte = db.Select(o.Ndc).From(o).Where(o.Ndc.Eq("B")).AsCte<Ranked>("ranked");
                    var ranked = new Ranked();
                    var rows = await db.Select(ranked.Ndc).With(cte).From(ranked).ToListAsync<NdcRow>();
                }
            }
            """;

        var diagnostics = AnalyzeValidSource([Tables, RankedTable, callSite], "Strict");

        Assert.Contains(diagnostics, d => d.Id == "MIZ002");
    }

    [Fact]
    public void A_mutable_cte_field_is_rejected_even_with_no_reassignment_visible_in_this_file()
    {
        // A mutable field could be reassigned from any method -- or, if it's
        // accessible, from an entirely different type -- which no syntax walk
        // over one file can ever exhaustively rule out. It must be rejected
        // unconditionally, not merely when a reassignment happens to be found.
        const string callSite = """
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Ir;
            using Mizzle.Postgres;

            namespace Demo;

            public sealed record NdcRow(string Ndc);

            public static class MutableFieldCteQ
            {
                private static readonly PostgresDb Db = null!;
                private static readonly Orders O = new Orders();
                private static CteClause _cte = Db.Select(O.Ndc).From(O).AsCte<Ranked>("ranked");

                public static async Task Run()
                {
                    var ranked = new Ranked();
                    var rows = await Db.Select(ranked.Ndc).With(_cte).From(ranked).ToListAsync<NdcRow>();
                }
            }
            """;

        var diagnostics = AnalyzeValidSource([Tables, RankedTable, callSite], "Strict");

        Assert.Contains(diagnostics, d => d.Id == "MIZ002");
    }

    [Fact]
    public void A_readonly_cte_field_reassigned_in_a_constructor_is_rejected()
    {
        // A readonly field can only be assigned in its own initializer or in a
        // constructor of the declaring type -- this one is assigned in both (the
        // constructor conditionally overrides it), so the initializer alone
        // cannot be trusted: which value a real instance ends up running depends
        // on a runtime branch this walker cannot evaluate. Db/O are static: an
        // instance field initializer cannot reference another instance member of
        // the same type (CS0236), so the shared PostgresDb/Orders have to be
        // static for _cte's own initializer to be legal C# at all.
        const string callSite = """
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Ir;
            using Mizzle.Postgres;

            namespace Demo;

            public sealed record NdcRow(string Ndc);

            public sealed class ReadonlyFieldReassignedCteQ
            {
                private static readonly PostgresDb Db = null!;
                private static readonly Orders O = new Orders();
                private readonly CteClause _cte = Db.Select(O.Ndc).From(O).AsCte<Ranked>("ranked");

                public ReadonlyFieldReassignedCteQ(string variant)
                {
                    if (variant == "b")
                    {
                        _cte = Db.Select(O.Ndc).From(O).Where(O.Ndc.Eq("B")).AsCte<Ranked>("ranked");
                    }
                }

                public async Task Run()
                {
                    var ranked = new Ranked();
                    var rows = await Db.Select(ranked.Ndc).With(_cte).From(ranked).ToListAsync<NdcRow>();
                }
            }
            """;

        var diagnostics = AnalyzeValidSource([Tables, RankedTable, callSite], "Strict");

        Assert.Contains(diagnostics, d => d.Id == "MIZ002");
    }

    [Fact]
    public void A_readonly_cte_field_reassigned_in_another_partial_declarations_constructor_is_rejected()
    {
        // A partial class can spread its declaration -- and its constructors --
        // across multiple files/syntax trees. The field (and the code that
        // consumes it) lives in one; the constructor that reassigns it lives in
        // a completely separate syntax tree. Scanning only the field's own
        // declaring syntax tree would miss this constructor entirely.
        const string fieldAndConsumer = """
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Ir;
            using Mizzle.Postgres;

            namespace Demo;

            public sealed record NdcRow(string Ndc);

            public sealed partial class SplitReadonlyFieldCteQ
            {
                private static readonly PostgresDb Db = null!;
                private static readonly Orders O = new Orders();
                private readonly CteClause _cte = Db.Select(O.Ndc).From(O).AsCte<Ranked>("ranked");

                public async Task Run()
                {
                    var ranked = new Ranked();
                    var rows = await Db.Select(ranked.Ndc).With(_cte).From(ranked).ToListAsync<NdcRow>();
                }
            }
            """;

        const string constructorPart = """
            namespace Demo;

            public sealed partial class SplitReadonlyFieldCteQ
            {
                public SplitReadonlyFieldCteQ(string variant)
                {
                    if (variant == "b")
                    {
                        _cte = Db.Select(O.Ndc).From(O).Where(O.Ndc.Eq("B")).AsCte<Ranked>("ranked");
                    }
                }
            }
            """;

        var diagnostics = AnalyzeValidSource([Tables, RankedTable, fieldAndConsumer, constructorPart], "Strict");

        Assert.Contains(diagnostics, d => d.Id == "MIZ002");
    }

    [Fact]
    public void A_readonly_cte_field_never_reassigned_still_bakes()
    {
        // The positive control: a readonly field assigned only once, at its
        // declaration, is exactly as provable as a local assigned once -- it
        // must not be swept up by the fix for the mutable/reassigned cases.
        const string callSite = """
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Ir;
            using Mizzle.Postgres;

            namespace Demo;

            public sealed record NdcRow(string Ndc);

            public sealed class ReadonlyFieldCteQ
            {
                private static readonly PostgresDb Db = null!;
                private static readonly Orders O = new Orders();
                private readonly CteClause _cte = Db.Select(O.Ndc).From(O).AsCte<Ranked>("ranked");

                public async Task Run()
                {
                    var ranked = new Ranked();
                    var rows = await Db.Select(ranked.Ndc).With(_cte).From(ranked).ToListAsync<NdcRow>();
                }
            }
            """;

        var diagnostics = AnalyzeValidSource([Tables, RankedTable, callSite], "Strict");

        Assert.DoesNotContain(diagnostics, d => d.Id == "MIZ002");
    }

    [Fact]
    public void An_aggregate_can_now_be_a_typed_cte_column()
    {
        // Previously every aggregate resolved to CLR type "object", which has no
        // column factory -- MIZ016 fired for any CTE that tried to project one.
        // Sum's argument column (decimal) drives the generated column's type.
        const string tables = """
            using Mizzle.Postgres;

            namespace Demo;

            public sealed class Orders : PgTable<Orders>
            {
                public Orders() : base("orders", "public") { }
                public PgColumn<System.Guid> CustomerId { get; } = Uuid("customer_id").NotNull();
                public PgColumn<decimal> Total { get; } = Numeric("total").NotNull();
            }
            """;

        const string callSite = """
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public sealed record CustomerRevenue(System.Guid CustomerId, decimal? Revenue);

            public static class RevenueQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var o = new Orders();
                    var cte = db.Select(o.CustomerId, Sql.Sum(o.Total).As("Revenue"))
                        .From(o)
                        .GroupBy(o.CustomerId)
                        .AsCte<CustomerRevenueCte>("customer_revenue");
                }
            }
            """;

        var result = GeneratorTestHost.Run(tables, callSite);
        var generated = GeneratorTestHost.Generated(result);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "MIZ015" or "MIZ016");
        Assert.Contains("public sealed class CustomerRevenueCte", generated, StringComparison.Ordinal);
        Assert.Contains(
            """global::Mizzle.Postgres.PgColumn<decimal> Revenue { get; } = Numeric("Revenue");""",
            generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Sum_of_an_int_column_promotes_the_cte_column_to_long_not_int_or_object()
    {
        // Postgres's SUM(integer) is bigint, not int -- a generated column typed
        // PgColumn<int> would read the wrong width; PgColumn<object> (the old,
        // always-untyped fallback) would compile but lose all type safety.
        const string tables = """
            using Mizzle.Postgres;

            namespace Demo;

            public sealed class Orders : PgTable<Orders>
            {
                public Orders() : base("orders", "public") { }
                public PgColumn<System.Guid> CustomerId { get; } = Uuid("customer_id").NotNull();
                public PgColumn<int> Quantity { get; } = Integer("quantity").NotNull();
            }
            """;

        const string callSite = """
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public static class QuantityQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var o = new Orders();
                    var cte = db.Select(o.CustomerId, Sql.Sum(o.Quantity).As("TotalQuantity"))
                        .From(o)
                        .GroupBy(o.CustomerId)
                        .AsCte<CustomerQuantityCte>("customer_quantity");
                }
            }
            """;

        var result = GeneratorTestHost.Run(tables, callSite);
        var generated = GeneratorTestHost.Generated(result);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "MIZ015" or "MIZ016");
        Assert.Contains(
            """global::Mizzle.Postgres.PgColumn<long> TotalQuantity { get; } = BigInt("TotalQuantity");""",
            generated, StringComparison.Ordinal);
    }
}
