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
        Assert.False(Bakes("""
            var q = db.Select(p.PersonId).From(p);
            if (flag) q = q.Where(p.Status.Eq("open"));
            var rows = await q.ToListAsync<Row>();
            """));
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
}
