using Microsoft.CodeAnalysis;

namespace Mizzle.Generators.Tests;

// Projection targets are routinely internal DTOs. A public mapper returning one
// is CS0050, so the generated code would not compile.
public sealed class InternalProjectionTargetTests
{
    private const string Tables = """
        using System;
        using Mizzle.Postgres;

        namespace Demo;

        public sealed class People : PgTable<People>
        {
            public People() : base("people", "public") { }
            public PgColumn<Guid> PersonId { get; } = Uuid("person_id").NotNull();
        }
        """;

    [Fact]
    public void An_internal_projection_target_compiles()
    {
        const string callSite = """
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            internal sealed class InternalRow
            {
                public Guid PersonId { get; set; }
            }

            internal static class Q
            {
                public static async Task Run(PostgresDb db)
                {
                    var p = new People();
                    var rows = await db.Select(p.PersonId).From(p).ToListAsync<InternalRow>();
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, callSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void A_public_projection_target_still_compiles()
    {
        const string callSite = """
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public sealed class PublicRow
            {
                public Guid PersonId { get; set; }
            }

            public static class PubQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var p = new People();
                    var rows = await db.Select(p.PersonId).From(p).ToListAsync<PublicRow>();
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, callSite);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
