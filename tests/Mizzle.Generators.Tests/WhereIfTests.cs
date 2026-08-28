using Microsoft.CodeAnalysis;

namespace Mizzle.Generators.Tests;

// A conditional filter is how every real reader is written. WhereIf keeps such a
// query on the compiled path by baking one SQL variant per combination and
// selecting at run time.
public sealed class WhereIfTests
{
    private const string Tables = """
        using System;
        using Mizzle.SqlServer;

        namespace Demo;

        public sealed class Meds : SqlTable<Meds>
        {
            public Meds() : base("patient_medication", "dbo") { }
            public SqlColumn<Guid> UniqId { get; } = UniqueIdentifier("uniq_id").NotNull();
            public SqlColumn<Guid> PersonId { get; } = UniqueIdentifier("person_id").NotNull();
            public SqlColumn<string> PracticeId { get; } = Char("practice_id", 4).NotNull();
            public SqlColumn<string> DateStopped { get; } = VarChar("date_stopped", 8);
        }
        """;

    private const string CallSite = """
        using System;
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.SqlServer;

        namespace Demo;

        internal sealed class MedRow { public Guid UniqId { get; set; } }

        internal static class Q
        {
            public static async Task Run(SqlDb db, Guid patientId, bool activeOnly, bool enterpriseChart, string practice)
            {
                var m = new Meds();
                var rows = await db.Select(m.UniqId)
                    .From(m)
                    .Where(m.PersonId.Eq(patientId))
                    .WhereIf(activeOnly, m.DateStopped.Eq("00000000"))
                    .WhereIf(!enterpriseChart, m.PracticeId.Eq(practice))
                    .ToListAsync<MedRow>();
            }
        }
        """;

    [Fact]
    public void A_conditional_query_still_bakes()
    {
        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, CallSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Every_combination_of_conditionals_is_baked()
    {
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(Tables, CallSite));

        // Two conditionals -> four shapes, selected by the builder's mask.
        Assert.Contains("builder.ConditionalMask switch", generated, StringComparison.Ordinal);
        foreach (var mask in new[] { "0UL =>", "1UL =>", "2UL =>", "3UL =>" })
        {
            Assert.Contains(mask, generated, StringComparison.Ordinal);
        }

        // The mandatory predicate is in every shape; the conditionals are not.
        // Both applied, and the bind slots renumber per shape.
        Assert.Contains("[date_stopped] = @p1", generated, StringComparison.Ordinal);
        Assert.Contains("[practice_id] = @p2", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Beyond_the_cap_the_query_falls_back_to_runtime()
    {
        var manyConditionals = string.Concat(Enumerable.Range(0, 5)
            .Select(i => $"                    .WhereIf(flag, m.DateStopped.Eq(\"{i}\"))\n"));
        var callSite = $$"""
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            internal sealed class ManyRow { public Guid UniqId { get; set; } }

            internal static class ManyQ
            {
                public static async Task Run(SqlDb db, bool flag)
                {
                    var m = new Meds();
                    var rows = await db.Select(m.UniqId)
                        .From(m)
            {{manyConditionals}}            .ToListAsync<ManyRow>();
                }
            }
            """;

        // Five conditionals is 32 shapes; past the cap it is not worth baking.
        var result = GeneratorTestHost.Run(Tables, callSite);
        Assert.DoesNotContain("ManyRowIntoMapper", GeneratorTestHost.Generated(result), StringComparison.Ordinal);
    }
}
