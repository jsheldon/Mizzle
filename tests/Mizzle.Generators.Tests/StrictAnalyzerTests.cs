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
}
