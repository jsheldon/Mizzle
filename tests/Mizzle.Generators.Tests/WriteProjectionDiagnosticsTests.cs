using Microsoft.CodeAnalysis;

namespace Mizzle.Generators.Tests;

// Write-side typed projections are mapped at runtime, but the same mistakes the
// select path catches at build time should be caught here too.
public sealed class WriteProjectionDiagnosticsTests
{
    private const string Tables = """
        using Mizzle.SqlServer;

        namespace Demo;

        public sealed class Widgets : SqlTable<Widgets>
        {
            public Widgets() : base("widgets", "dbo") { }
            public SqlColumn<System.Guid> WidgetId { get; } = UniqueIdentifier("widget_id").NotNull();
            public SqlColumn<string> Label { get; } = VarChar("label", 50);
            public SqlColumn<int> Qty { get; } = Int("qty").NotNull();
        }
        """;

    private static string CallSite(string builder, string rowMembers, string returning) => $$"""
        using System;
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.SqlServer;

        namespace Demo;

        public class WriteRow
        {
            {{rowMembers}}
        }

        public static class WriteQ
        {
            public static async Task Run(SqlDb db)
            {
                var w = new Widgets();
                var rows = await {{builder}}
                    .Returning({{returning}})
                    .ToListAsync<WriteRow>();
            }
        }
        """;

    [Fact]
    public void Update_returning_into_a_missing_member_reports_MIZ003()
    {
        var source = CallSite(
            "db.Update(w).Set(w.Qty, 1)",
            "public Guid WidgetId { get; set; }",
            "w.WidgetId, w.Label");

        var result = GeneratorTestHost.Run(Tables, source);
        Assert.Contains(result.Diagnostics, d => d.Id == "MIZ003");
    }

    [Fact]
    public void Delete_returning_into_a_wrongly_typed_member_reports_MIZ010()
    {
        var source = CallSite(
            "db.DeleteFrom(w)",
            "public int WidgetId { get; set; } public string? Label { get; set; }",
            "w.WidgetId, w.Label");

        var result = GeneratorTestHost.Run(Tables, source);
        Assert.Contains(result.Diagnostics, d => d.Id == "MIZ010");
    }

    [Fact]
    public void Insert_returning_a_nullable_column_into_a_non_nullable_member_reports_MIZ005()
    {
        var source = CallSite(
            "db.InsertInto(w).Value(w.Qty, 1)",
            "public Guid WidgetId { get; set; } public string Label { get; set; } = \"\";",
            "w.WidgetId, w.Label");

        var result = GeneratorTestHost.Run(Tables, source);
        Assert.Contains(result.Diagnostics, d => d.Id == "MIZ005");
    }

    [Fact]
    public void A_correct_write_projection_reports_nothing()
    {
        var source = CallSite(
            "db.Update(w).Set(w.Qty, 1)",
            "public Guid WidgetId { get; set; } public string? Label { get; set; }",
            "w.WidgetId, w.Label");

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, source);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Write_projections_are_validated_but_not_intercepted()
    {
        var source = CallSite(
            "db.Update(w).Set(w.Qty, 1)",
            "public Guid WidgetId { get; set; } public string? Label { get; set; }",
            "w.WidgetId, w.Label");

        // Runtime mapping still does the work -- no baked mapper for writes yet.
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(Tables, source));
        Assert.DoesNotContain("WriteRowIntoMapper", generated, StringComparison.Ordinal);
    }
}
