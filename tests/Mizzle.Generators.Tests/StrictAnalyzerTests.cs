using Microsoft.CodeAnalysis;

namespace Mizzle.Generators.Tests;

public sealed class StrictAnalyzerTests
{
    [Fact]
    public void Strict_mode_reports_MIZ002_for_dynamic_builder()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Data.Common;
            using System.Threading.Tasks;
            using Mizzle.Fluent;

            namespace Demo;

            public static class Queries
            {
                public static Task<IReadOnlyList<string>> List(SelectBuilder builder)
                {
                    return builder.ToListAsync(static r => r.GetString(0));
                }
            }
            """;

        var diagnostics = GeneratorTestHost.Analyze(source, queryMode: "Strict");
        Assert.Contains(diagnostics, d => d.Id == "MIZ002");
    }

    [Fact]
    public void Hybrid_mode_does_not_report_MIZ002_for_dynamic_builder()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Data.Common;
            using System.Threading.Tasks;
            using Mizzle.Fluent;

            namespace Demo;

            public static class Queries
            {
                public static Task<IReadOnlyList<string>> List(SelectBuilder builder)
                {
                    return builder.ToListAsync(static r => r.GetString(0));
                }
            }
            """;

        var diagnostics = GeneratorTestHost.Analyze(source, queryMode: "Hybrid");
        Assert.DoesNotContain(diagnostics, d => d.Id == "MIZ002");
    }
}
