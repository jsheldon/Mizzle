using Microsoft.CodeAnalysis;

namespace Mizzle.Generators.Tests;

// Sql.Or/Sql.And compose a WHERE condition out of several comparisons -- the
// legacy-blessed "active medication" predicate and a NULL-aware keyset seek
// both need this shape, and previously fell off the baked path entirely.
public sealed class WhereOrAndConditionTests
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
            public SqlColumn<string> StartDate { get; } = VarChar("start_date", 8);
            public SqlColumn<string> DateStopped { get; } = VarChar("date_stopped", 8);
        }
        """;

    private const string RowType = """
        internal sealed class MedRow { public Guid UniqId { get; set; } }
        """;

    [Fact]
    public void Or_of_two_comparisons_bakes_parenthesized()
    {
        const string callSite = $$"""
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            {{RowType}}

            internal static class OrQ
            {
                public static async Task Run(SqlDb db, Guid patientId)
                {
                    var m = new Meds();
                    var rows = await db.Select(m.UniqId)
                        .From(m)
                        .Where(m.PersonId.Eq(patientId))
                        .Where(Sql.Or(m.DateStopped.IsNull(), m.DateStopped.Eq("00000000")))
                        .ToListAsync<MedRow>();
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, callSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains(
            "([patient_medication].[date_stopped] IS NULL OR [patient_medication].[date_stopped] = @p1)",
            generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Four_way_or_preserves_argument_order_for_bind_numbering()
    {
        const string callSite = $$"""
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            {{RowType}}

            internal static class FourOrQ
            {
                public static async Task Run(SqlDb db, Guid patientId)
                {
                    var m = new Meds();
                    var rows = await db.Select(m.UniqId)
                        .From(m)
                        .Where(m.PersonId.Eq(patientId))
                        .Where(Sql.Or(
                            m.DateStopped.IsNull(),
                            m.DateStopped.Eq(""),
                            m.DateStopped.Eq("00000000"),
                            m.DateStopped.Eq("99999999")))
                        .ToListAsync<MedRow>();
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, callSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generated = GeneratorTestHost.Generated(result);
        // Binds after the leading @p0 (patientId) number left-to-right through
        // the OR's arguments, in the order they were written.
        Assert.Contains(
            "([patient_medication].[date_stopped] IS NULL OR [patient_medication].[date_stopped] = @p1 " +
            "OR [patient_medication].[date_stopped] = @p2 OR [patient_medication].[date_stopped] = @p3)",
            generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Or_of_an_and_group_nests_correctly()
    {
        // The NULL-aware keyset seek shape: (a < x) OR (a IS NULL) OR (a = x AND b > y).
        const string callSite = $$"""
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            {{RowType}}

            internal static class SeekQ
            {
                public static async Task Run(SqlDb db, string afterStart, Guid afterId)
                {
                    var m = new Meds();
                    var rows = await db.Select(m.UniqId)
                        .From(m)
                        .Where(Sql.Or(
                            m.StartDate.Lt(afterStart),
                            m.StartDate.IsNull(),
                            Sql.And(m.StartDate.Eq(afterStart), m.UniqId.Gt(afterId))))
                        .ToListAsync<MedRow>();
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, callSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains(
            "([patient_medication].[start_date] < @p0 OR [patient_medication].[start_date] IS NULL " +
            "OR ([patient_medication].[start_date] = @p1 AND [patient_medication].[uniq_id] > @p2))",
            generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Or_predicate_survives_whereif_masking()
    {
        const string callSite = $$"""
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            {{RowType}}

            internal static class WhereIfOrQ
            {
                public static async Task Run(SqlDb db, Guid patientId, bool activeOnly)
                {
                    var m = new Meds();
                    var rows = await db.Select(m.UniqId)
                        .From(m)
                        .Where(m.PersonId.Eq(patientId))
                        .WhereIf(activeOnly, Sql.Or(m.DateStopped.IsNull(), m.DateStopped.Eq("00000000")))
                        .ToListAsync<MedRow>();
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, callSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains("builder.ConditionalMask switch", generated, StringComparison.Ordinal);
        // Applied-variant shape still carries the OR group, parenthesized.
        Assert.Contains(
            "([patient_medication].[date_stopped] IS NULL OR [patient_medication].[date_stopped] = @p1)",
            generated, StringComparison.Ordinal);
        // Not-applied variant has neither leg of the OR group.
        Assert.Contains(
            "0UL => \"SELECT [patient_medication].[uniq_id] FROM [dbo].[patient_medication] AS [patient_medication] " +
            "WHERE [patient_medication].[person_id] = @p0\"",
            generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Or_predicate_built_into_a_local_still_bakes()
    {
        // Real readers compute the predicate once, ahead of the chain, so it
        // reads clearly and so WhereIf still gets an eagerly-built Expr even
        // when its condition is false. That local must resolve the same as an
        // inline Sql.Or(...) would.
        const string callSite = $$"""
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            {{RowType}}

            internal static class LocalOrQ
            {
                public static async Task Run(SqlDb db, Guid patientId, bool activeOnly)
                {
                    var m = new Meds();
                    var activePredicate = Sql.Or(m.DateStopped.IsNull(), m.DateStopped.Eq("00000000"));
                    var rows = await db.Select(m.UniqId)
                        .From(m)
                        .Where(m.PersonId.Eq(patientId))
                        .WhereIf(activeOnly, activePredicate)
                        .ToListAsync<MedRow>();
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, callSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains(
            "([patient_medication].[date_stopped] IS NULL OR [patient_medication].[date_stopped] = @p1)",
            generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_eq_of_rendered_operands_bakes_inside_an_or_group()
    {
        // Sql.Eq(left, right) is the escape hatch for a comparison whose left
        // side is not a bare column -- rtrim(col) = '' cannot be written as
        // col.Eq(''), so the legacy-blessed active-medication predicate needs
        // the free-standing form. Sql.Value("") is an Expr but still means
        // "bind this literal", not "render this as SQL".
        const string callSite = $$"""
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            {{RowType}}

            internal static class RTrimEqQ
            {
                public static async Task Run(SqlDb db, Guid patientId)
                {
                    var m = new Meds();
                    var rows = await db.Select(m.UniqId)
                        .From(m)
                        .Where(m.PersonId.Eq(patientId))
                        .Where(Sql.Or(
                            m.DateStopped.IsNull(),
                            Sql.Eq(TSql.RTrim(m.DateStopped), Sql.Value(""))))
                        .ToListAsync<MedRow>();
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, callSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains(
            "([patient_medication].[date_stopped] IS NULL OR rtrim([patient_medication].[date_stopped]) = @p1)",
            generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Unbakeable_operand_inside_or_falls_back_to_runtime()
    {
        const string callSite = $$"""
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            internal sealed class UnbakeableRow { public Guid UniqId { get; set; } }

            internal static class BadOrQ
            {
                public static async Task Run(SqlDb db, string lo, string hi)
                {
                    var m = new Meds();
                    var rows = await db.Select(m.UniqId)
                        .From(m)
                        .Where(Sql.Or(m.DateStopped.IsNull(), m.DateStopped.Between(lo, hi)))
                        .ToListAsync<UnbakeableRow>();
                }
            }
            """;

        var result = GeneratorTestHost.Run(Tables, callSite);
        // Between is not an operator ResolveCondition understands, so the OR
        // group -- and the whole chain -- cannot be baked; it falls back
        // silently to the runtime path rather than erroring (Strict mode is
        // what turns that into a build error, and this host does not enable it).
        Assert.DoesNotContain("UnbakeableRowIntoMapper", GeneratorTestHost.Generated(result), StringComparison.Ordinal);
    }
}
