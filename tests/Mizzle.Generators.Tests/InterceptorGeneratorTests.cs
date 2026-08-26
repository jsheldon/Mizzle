namespace Mizzle.Generators.Tests;

public sealed class InterceptorGeneratorTests
{
    private const string UsersTable = """
        using Mizzle.Postgres;

        namespace Demo;

        public sealed class Users : PgTable<Users>
        {
            public Users() : base("users", "public", "u") { }
            public PgColumn<int> Id { get; } = Identity("id").PrimaryKey();
            public PgColumn<string> Email { get; } = Text("email").NotNull();
        }
        """;

    private const string CallSite = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Mizzle.Postgres;

        namespace Demo;

        public static class Q
        {
            public static async Task Run(PostgresDb db, string email)
            {
                var users = new Users();
                _ = await db.Select(users.Email)
                    .From(users.ToFrom())
                    .Where(users.Email, email)
                    .Limit(10)
                    .ToListAsync(static r => r.GetString(0));
            }
        }
        """;

    private static string RunGenerator(params string[] sources)
        => GeneratorTestHost.Generated(GeneratorTestHost.Run(string.Join("\n", sources)));

    [Fact]
    public void Bakes_visible_postgres_chain_to_constant_sql()
    {
        var generated = RunGenerator(UsersTable, CallSite);
        Assert.Contains("InterceptsLocation", generated, StringComparison.Ordinal);
        Assert.Contains("ToListPrecompiledAsync", generated, StringComparison.Ordinal);
        Assert.Contains(
            "SELECT \\\"u\\\".\\\"email\\\" FROM \\\"public\\\".\\\"users\\\" AS \\\"u\\\" WHERE \\\"u\\\".\\\"email\\\" = $1 LIMIT 10",
            generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Baked_sql_matches_runtime_emitter()
    {
        var bag = new Mizzle.Ir.ParamBag();
        var email = new Mizzle.Ir.ColumnRef("u", "email", typeof(string));
        var p = bag.Add("x", typeof(string));
        var ir = new Mizzle.Ir.SelectQuery(
            Select: [new Mizzle.Ir.SelectItem(email, null)],
            From: new Mizzle.Ir.FromSource("users", "public", "u"),
            Joins: [],
            Where: new Mizzle.Ir.BinaryExpr(Mizzle.Ir.BinaryOp.Eq, email, p),
            OrderBy: [], Limit: 10, Offset: null, Distinct: false,
            With: [], RecursiveWith: false, UnionAll: []);
        var runtime = new Mizzle.Postgres.PgEmitter().Emit(ir, bag).Sql;
        var generated = RunGenerator(UsersTable, CallSite);
        var escaped = runtime.Replace("\"", "\\\"", StringComparison.Ordinal);
        Assert.Contains(escaped, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Dynamic_SelectBuilder_is_not_intercepted()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Mizzle.Fluent;

            namespace Demo;

            public static class Q
            {
                public static Task<IReadOnlyList<string>> List(SelectBuilder builder)
                    => builder.ToListAsync(static r => r.GetString(0));
            }
            """;
        var generated = RunGenerator(UsersTable, source);
        Assert.DoesNotContain("InterceptsLocation", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Variable_limit_is_not_intercepted()
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
        var generated = RunGenerator(UsersTable, site);
        Assert.DoesNotContain("InterceptsLocation", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_server_chain_bakes_bracket_sql()
    {
        const string sqlTable = """
            using Mizzle.SqlServer;

            namespace Demo;

            public sealed class SqlUsers : SqlTable<SqlUsers>
            {
                public SqlUsers() : base("users", "dbo", "u") { }
                public SqlColumn<string> Email { get; } = NVarChar("email", 255);
            }
            """;
        const string site = """
            using System.Threading.Tasks;
            using Mizzle.SqlServer;

            namespace Demo;

            public static class Q
            {
                public static async Task Run(SqlDb db, string email)
                {
                    var users = new SqlUsers();
                    _ = await db.Select(users.Email).From(users.ToFrom()).Where(users.Email, email)
                        .ToListAsync(static r => r.GetString(0));
                }
            }
            """;
        var generated = RunGenerator(sqlTable, site);
        Assert.Contains(
            "SELECT [u].[email] FROM [dbo].[users] AS [u] WHERE [u].[email] = @p0",
            generated, StringComparison.Ordinal);
    }
}
