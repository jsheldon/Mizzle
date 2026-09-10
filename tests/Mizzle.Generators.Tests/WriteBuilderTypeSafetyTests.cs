using Microsoft.CodeAnalysis;

namespace Mizzle.Generators.Tests;

// Set/Where/Value used to accept (IColumn, object?), so a mismatched value type
// compiled and only failed at the database. They are generic over the column's
// own type now, matching the column's own strongly typed Eq(T).
public sealed class WriteBuilderTypeSafetyTests
{
    private const string Tables = """
        using Mizzle.SqlServer;

        namespace Demo;

        public sealed class Widgets : SqlTable<Widgets>
        {
            public Widgets() : base("widgets", "dbo") { }
            public SqlColumn<int> Qty { get; } = Int("qty").NotNull();
            public SqlColumn<string> Label { get; } = VarChar("label", 50);
        }
        """;

    private static string CallSite(string statement) => $$"""
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.SqlServer;

        namespace Demo;

        public static class WriteQ
        {
            public static async Task Run(SqlDb db)
            {
                var w = new Widgets();
                {{statement}}
            }
        }
        """;

    [Fact]
    public void Set_with_a_mismatched_value_type_does_not_compile()
    {
        var (_, diagnostics) = GeneratorTestHost.RunAndCompile(
            Tables, CallSite("""await db.Update(w).Set(w.Qty, "wrong").ExecuteAsync();"""));

        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Set_with_a_matching_value_type_compiles_cleanly()
    {
        var (_, diagnostics) = GeneratorTestHost.RunAndCompile(
            Tables, CallSite("await db.Update(w).Set(w.Qty, 1).ExecuteAsync();"));

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Value_with_a_mismatched_value_type_does_not_compile()
    {
        var (_, diagnostics) = GeneratorTestHost.RunAndCompile(
            Tables, CallSite("""await db.InsertInto(w).Value(w.Qty, "wrong").ExecuteAsync();"""));

        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Value_with_a_matching_value_type_compiles_cleanly()
    {
        var (_, diagnostics) = GeneratorTestHost.RunAndCompile(
            Tables, CallSite("await db.InsertInto(w).Value(w.Qty, 1).ExecuteAsync();"));

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Builder_Where_with_a_mismatched_value_type_does_not_compile()
    {
        var (_, diagnostics) = GeneratorTestHost.RunAndCompile(
            Tables, CallSite("""await db.Update(w).Set(w.Qty, 1).Where(w.Label, 5).ExecuteAsync();"""));

        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Builder_Where_with_a_matching_value_type_compiles_cleanly()
    {
        var (_, diagnostics) = GeneratorTestHost.RunAndCompile(
            Tables, CallSite("""await db.Update(w).Set(w.Qty, 1).Where(w.Label, "x").ExecuteAsync();"""));

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SelectBuilder_Where_with_a_mismatched_value_type_does_not_compile()
    {
        var (_, diagnostics) = GeneratorTestHost.RunAndCompile(
            Tables, CallSite("""await db.Select(w.Label).From(w).Where(w.Qty, "wrong").ToListAsync(static r => r.GetString(0));"""));

        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SelectBuilder_Where_with_a_matching_value_type_compiles_cleanly()
    {
        var (_, diagnostics) = GeneratorTestHost.RunAndCompile(
            Tables, CallSite("await db.Select(w.Label).From(w).Where(w.Qty, 1).ToListAsync(static r => r.GetString(0));"));

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
