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

    private const string UsersTable = """
        using Mizzle.Postgres;

        namespace Demo;

        public sealed class Users : PgTable<Users>
        {
            public Users() : base("users", "public") { }
            public PgColumn<string> Email { get; } = Text("email");
        }
        """;

    [Fact]
    public void Strict_mode_reports_MIZ002_for_variable_limit()
    {
        const string site = """
            using System.Threading.Tasks;
            using Mizzle.Postgres;

            namespace Demo;

            public static class Q
            {
                public static async Task Run(PostgresDb db, int n)
                {
                    var users = new Users();
                    _ = await db.Select(users.Email).From(users.ToFrom()).Limit(n)
                        .ToListAsync(static r => r.GetString(0));
                }
            }
            """;
        var diagnostics = GeneratorTestHost.Analyze(UsersTable + "\n" + site, queryMode: "Strict");
        Assert.Contains(diagnostics, d => d.Id == "MIZ002");
    }

    [Fact]
    public void Strict_mode_accepts_fully_visible_chain()
    {
        const string site = """
            using System.Threading.Tasks;
            using Mizzle.Postgres;

            namespace Demo;

            public static class Q
            {
                public static async Task Run(PostgresDb db, string email)
                {
                    var users = new Users();
                    _ = await db.Select(users.Email).From(users.ToFrom()).Where(users.Email, email).Limit(10)
                        .ToListAsync(static r => r.GetString(0));
                }
            }
            """;
        var diagnostics = GeneratorTestHost.Analyze(UsersTable + "\n" + site, queryMode: "Strict");
        Assert.DoesNotContain(diagnostics, d => d.Id == "MIZ002");
    }

    private const string PeopleTable = """
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

    // ProjectionGenerator resolves a reassigned local (q = q.Where(...)) via the
    // dynamic-mapper path, forwarding to a delegate-based overload with a real
    // generated mapper -- it never falls back to a delegate-free runtime throw.
    // Strict mode must accept that resolution too, not just full SQL baking: it
    // would otherwise reject a call site the generator itself already made safe.
    [Fact]
    public void Strict_mode_accepts_a_reassigned_local_resolved_by_the_dynamic_mapper()
    {
        const string site = """
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            internal sealed class Row { public System.Guid PersonId { get; set; } }

            internal static class Q
            {
                public static async Task Run(SqlDb db, bool flag)
                {
                    var p = new People();
                    var q = db.Select(p.PersonId).From(p);
                    if (flag) q = q.Where(p.Status.Eq("open"));
                    var rows = await q.ToListAsync<Row>();
                }
            }
            """;

        var diagnostics = GeneratorTestHost.Analyze(PeopleTable + "\n" + site, queryMode: "Strict");
        Assert.DoesNotContain(diagnostics, d => d.Id == "MIZ002");
    }

    // Same shape, but the reassignment re-invokes Select -- ProjectionGenerator
    // refuses to trust that the column list still matches (MIZ014), so Strict
    // mode must still reject it: there is no generated mapper to fall back to.
    [Fact]
    public void Strict_mode_still_rejects_a_reassignment_that_re_selects()
    {
        const string site = """
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            internal sealed class Row { public System.Guid PersonId { get; set; } }

            internal static class Q
            {
                public static async Task Run(SqlDb db, bool flag)
                {
                    var p = new People();
                    var q = db.Select(p.PersonId).From(p);
                    if (flag) q = q.Select(p.PersonId, p.Status).From(p);
                    var rows = await q.ToListAsync<Row>();
                }
            }
            """;

        var diagnostics = GeneratorTestHost.Analyze(PeopleTable + "\n" + site, queryMode: "Strict");
        Assert.Contains(diagnostics, d => d.Id == "MIZ002");
    }
}
