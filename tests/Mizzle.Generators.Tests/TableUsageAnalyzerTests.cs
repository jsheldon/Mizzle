namespace Mizzle.Generators.Tests;

public sealed class TableUsageAnalyzerTests
{
    private const string WithParameterless = """
        using Mizzle.SqlServer;

        namespace Demo;

        public sealed class People : SqlTable<People>
        {
            public People() : base("people", "dbo") { }
            public SqlColumn<int> Id { get; } = Int("id").NotNull();
        }

        public static class Q
        {
            public static People Aliased() => new People().WithAlias("p2");
        }
        """;

    private const string WithoutParameterless = """
        using Mizzle.SqlServer;

        namespace Demo;

        public sealed class People : SqlTable<People>
        {
            public People(string schema) : base("people", schema) { }
            public SqlColumn<int> Id { get; } = Int("id").NotNull();
        }

        public static class Q
        {
            public static People Aliased() => new People("dbo").WithAlias("p2");
        }
        """;

    [Fact]
    public void WithAlias_on_a_table_without_a_parameterless_constructor_reports_MIZ012()
    {
        // Activator.CreateInstance would throw at query time; the call site is
        // visible now.
        var diagnostics = GeneratorTestHost.Analyze(WithoutParameterless, null);
        var diagnostic = Assert.Single(diagnostics, d => d.Id == "MIZ012");
        Assert.Contains("People", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void WithAlias_on_a_normal_table_reports_nothing()
    {
        var diagnostics = GeneratorTestHost.Analyze(WithParameterless, null);
        Assert.DoesNotContain(diagnostics, d => d.Id == "MIZ012");
    }
}
