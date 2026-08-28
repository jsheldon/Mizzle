namespace Mizzle.Generators.Tests;

// Strict must look at every terminator the generator can bake, and leave alone the
// ones it cannot. Missing a terminator reads as a pass, which is worse than no
// Strict mode at all.
public sealed class StrictTerminatorTests
{
    private static string Source(string terminator, string alias) => $$"""
        using System;
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.Postgres;

        namespace Demo;

        public sealed class People : PgTable<People>
        {
            public People() : base("people", "public") { }
            public PgColumn<Guid> PersonId { get; } = Uuid("person_id").NotNull();
        }

        public record PersonRow(Guid Id);

        public static class Q
        {
            public static string Dynamic = "Id";

            public static async Task Run(PostgresDb db)
            {
                var p = new People();
                var rows = await db.Select(p.PersonId.As({{alias}}))
                    .From(p)
                    .{{terminator}}<PersonRow>();
            }
        }
        """;

    [Theory]
    [InlineData("ToListAsync")]
    [InlineData("FirstAsync")]
    [InlineData("FirstOrDefaultAsync")]
    [InlineData("SingleAsync")]
    [InlineData("SingleOrDefaultAsync")]
    public void An_unbakeable_chain_reports_MIZ002_for_every_typed_terminator(string terminator)
    {
        var diagnostics = GeneratorTestHost.Analyze(Source(terminator, "Dynamic"), "Strict");
        Assert.Contains(diagnostics, d => d.Id == "MIZ002");
    }

    [Theory]
    [InlineData("ToListAsync")]
    [InlineData("FirstAsync")]
    [InlineData("FirstOrDefaultAsync")]
    [InlineData("SingleAsync")]
    [InlineData("SingleOrDefaultAsync")]
    public void A_bakeable_chain_reports_nothing_for_every_typed_terminator(string terminator)
    {
        var diagnostics = GeneratorTestHost.Analyze(Source(terminator, "\"Id\""), "Strict");
        Assert.DoesNotContain(diagnostics, d => d.Id == "MIZ002");
    }

    [Fact]
    public void Execute_async_is_outside_strict_because_writes_never_bake()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public sealed class People : PgTable<People>
            {
                public People() : base("people", "public") { }
                public PgColumn<Guid> PersonId { get; } = Uuid("person_id").NotNull();
                public PgColumn<int> Age { get; } = Integer("age").NotNull();
            }

            public static class WriteQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var p = new People();
                    await db.Update(p).Set(p.Age, 1).ExecuteAsync();
                }
            }
            """;

        var diagnostics = GeneratorTestHost.Analyze(source, "Strict");
        Assert.DoesNotContain(diagnostics, d => d.Id == "MIZ002");
    }
}
