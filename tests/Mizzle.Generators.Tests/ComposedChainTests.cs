using Microsoft.CodeAnalysis;

namespace Mizzle.Generators.Tests;

// Real readers build queries across statements. A chain that starts from a
// builder-valued local must still bake -- but only when the local is assigned
// once, or a dropped reassignment would silently change the SQL.
public sealed class ComposedChainTests
{
    private const string Tables = """
        using System;
        using Mizzle.SqlServer;

        namespace Demo;

        public sealed class People : SqlTable<People>
        {
            public People() : base("people", "dbo") { }
            public SqlColumn<Guid> PersonId { get; } = UniqueIdentifier("person_id").NotNull();
            public SqlColumn<string> Status { get; } = VarChar("status", 20).NotNull();
        }
        """;

    private static string Case(string body) => $$"""
        using System;
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.SqlServer;

        namespace Demo;

        internal sealed class Row { public Guid PersonId { get; set; } }

        internal static class Q
        {
            public static async Task Run(SqlDb db, bool flag)
            {
                var p = new People();
                {{body}}
            }
        }
        """;

    private static bool Bakes(string body)
        => GeneratorTestHost.Generated(GeneratorTestHost.Run(Tables, Case(body)))
            .Contains("RowIntoMapper", StringComparison.Ordinal);

    [Fact]
    public void A_chain_continued_from_a_local_bakes()
    {
        Assert.True(Bakes("""
            var q = db.Select(p.PersonId).From(p);
            var rows = await q.Where(p.Status.Eq("open")).ToListAsync<Row>();
            """));
    }

    [Fact]
    public void A_reassigned_local_does_not_bake()
    {
        // Following the declaration alone would bake SQL without the extra Where.
        // Bakes() now also matches the dynamic-mapper mapper class (see
        // A_reassigned_local_that_only_adds_a_where_still_gets_a_generated_mapper),
        // so the precise invariant this test documents is "never a literal SQL
        // string", not "nothing is generated at all".
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(Tables, Case("""
            var q = db.Select(p.PersonId).From(p);
            if (flag) q = q.Where(p.Status.Eq("open"));
            var rows = await q.ToListAsync<Row>();
            """)));
        Assert.DoesNotContain("ToListPrecompiledAsync", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_union_branch_held_in_a_local_bakes_inside_a_cte()
    {
        Assert.True(Bakes("""
            var a = db.Select(p.PersonId.As("person_id")).From(p).Where(p.Status.Eq("open"));
            var b = db.Select(p.PersonId.As("person_id")).From(p).Where(p.Status.Eq("closed"));
            var body = a.UnionAll(b).Build();
            var rows = await db.Select(p.PersonId).With(CteBuilder.Named("both", body)).From(p).ToListAsync<Row>();
            """));
    }

    // A reassigned local cannot bake SQL, but the reassignment only adds a Where --
    // it never touches the select list -- so the generator can still emit a typed
    // mapper and forward to the delegate-based overload for dynamic execution,
    // rather than leaving the caller with the delegate-free runtime throw.
    [Fact]
    public void A_reassigned_local_that_only_adds_a_where_still_gets_a_generated_mapper()
    {
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(Tables, Case("""
            var q = db.Select(p.PersonId).From(p);
            if (flag) q = q.Where(p.Status.Eq("open"));
            var rows = await q.ToListAsync<Row>();
            """)));

        Assert.Contains("RowIntoMapper", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("ToListPrecompiledAsync", generated, StringComparison.Ordinal);
        Assert.Contains(".ToListAsync(global::Mizzle.Generated.Projections.", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reassigned_local_that_only_adds_a_where_compiles_cleanly()
    {
        var (_, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, Case("""
            var q = db.Select(p.PersonId).From(p);
            if (flag) q = q.Where(p.Status.Eq("open"));
            var rows = await q.ToListAsync<Row>();
            """));

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    // Same reassignment shape, but the reassignment re-invokes Select itself --
    // the column list it would produce is not provably the same as the
    // declaration's, so this must stay a plain runtime throw (MIZ014), not a
    // mapper built from a select list that might no longer match.
    [Fact]
    public void A_reassignment_that_re_selects_does_not_get_a_generated_mapper()
    {
        var result = GeneratorTestHost.Run(Tables, Case("""
            var q = db.Select(p.PersonId).From(p);
            if (flag) q = q.Select(p.PersonId, p.Status).From(p);
            var rows = await q.ToListAsync<Row>();
            """));

        Assert.Contains(result.Diagnostics, d => d.Id == "MIZ014");
        Assert.DoesNotContain("RowIntoMapper", GeneratorTestHost.Generated(result), StringComparison.Ordinal);
    }
}
