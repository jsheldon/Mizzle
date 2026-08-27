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

    private const string JoinTables = """
        using Mizzle.Postgres;

        namespace Demo;

        public sealed class Persons : PgTable<Persons>
        {
            public Persons() : base("person", "public", "a") { }
            public PgColumn<System.Guid> PersonId { get; } = Uuid("person_id").PrimaryKey();
            public PgColumn<System.Guid> LanguageId { get; } = Uuid("language_id");
            public PgColumn<string> FirstName { get; } = Text("first_name").NotNull();
            public PgColumn<System.Guid> PracticeId { get; } = Uuid("practice_id").NotNull();
        }

        public sealed class MstrLists : PgTable<MstrLists>
        {
            public MstrLists() : base("mstr_lists", "public", "c") { }
            public PgColumn<System.Guid> ItemId { get; } = Uuid("mstr_list_item_id").PrimaryKey();
            public PgColumn<string> ItemDesc { get; } = Text("mstr_list_item_desc");
            public PgColumn<string> ListType { get; } = Text("mstr_list_type").NotNull();
        }
        """;

    private const string JoinedCallSite = """
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.Postgres;

        namespace Demo;

        public static class JoinedQ
        {
            public static async Task Run(PostgresDb db, System.Guid id, System.Guid practice)
            {
                var a = new Persons();
                var c = new MstrLists();
                _ = await db.Select(a.FirstName, c.ItemDesc)
                    .From(a)
                    .LeftJoin(c).On(a.LanguageId.Eq(c.ItemId), c.ListType.Eq("language"))
                    .Where(a.PersonId.Eq(id))
                    .Where(a.PracticeId.Eq(practice))
                    .ToListAsync(static r => r.GetString(0));
            }
        }
        """;

    private static string JoinedRuntimeSql()
    {
        var personId = new Mizzle.Ir.ColumnRef("a", "person_id", typeof(Guid));
        var practiceId = new Mizzle.Ir.ColumnRef("a", "practice_id", typeof(Guid));
        var ir = new Mizzle.Ir.SelectQuery(
            Select:
            [
                new Mizzle.Ir.SelectItem(new Mizzle.Ir.ColumnRef("a", "first_name", typeof(string)), null),
                new Mizzle.Ir.SelectItem(new Mizzle.Ir.ColumnRef("c", "mstr_list_item_desc", typeof(string)), null)
            ],
            From: new Mizzle.Ir.FromSource("person", "public", "a"),
            Joins:
            [
                new Mizzle.Ir.JoinClause(
                    Mizzle.Ir.JoinKind.Left,
                    new Mizzle.Ir.FromSource("mstr_lists", "public", "c"),
                    new Mizzle.Ir.BinaryExpr(
                        Mizzle.Ir.BinaryOp.And,
                        new Mizzle.Ir.BinaryExpr(
                            Mizzle.Ir.BinaryOp.Eq,
                            new Mizzle.Ir.ColumnRef("a", "language_id", typeof(Guid)),
                            new Mizzle.Ir.ColumnRef("c", "mstr_list_item_id", typeof(Guid))),
                        new Mizzle.Ir.BinaryExpr(
                            Mizzle.Ir.BinaryOp.Eq,
                            new Mizzle.Ir.ColumnRef("c", "mstr_list_type", typeof(string)),
                            new Mizzle.Ir.ValueExpr("language", typeof(string)))))
            ],
            Where: new Mizzle.Ir.BinaryExpr(
                Mizzle.Ir.BinaryOp.And,
                new Mizzle.Ir.BinaryExpr(Mizzle.Ir.BinaryOp.Eq, personId, new Mizzle.Ir.ValueExpr(Guid.Empty, typeof(Guid))),
                new Mizzle.Ir.BinaryExpr(Mizzle.Ir.BinaryOp.Eq, practiceId, new Mizzle.Ir.ValueExpr(Guid.Empty, typeof(Guid)))),
            OrderBy: [], Limit: null, Offset: null, Distinct: false,
            With: [], RecursiveWith: false, UnionAll: []);
        var (canonical, values) = Mizzle.Compile.Parameterizer.Run(ir);
        return new Mizzle.Postgres.PgEmitter().Emit(canonical, values).Sql;
    }

    [Fact]
    public void Bakes_joined_multi_table_chain_matching_runtime_emitter()
    {
        var generated = RunGenerator(JoinTables, JoinedCallSite);
        Assert.Contains("InterceptsLocation", generated, StringComparison.Ordinal);
        var escaped = JoinedRuntimeSql().Replace("\"", "\\\"", StringComparison.Ordinal);
        Assert.Contains(escaped, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_eq_where_condition_is_not_baked()
    {
        const string site = """
            using System.Threading.Tasks;
            using Mizzle.Postgres;

            namespace Demo;

            public static class Q2
            {
                public static async Task Run(PostgresDb db)
                {
                    var a = new Persons();
                    _ = await db.Select(a.FirstName).From(a).Where(a.FirstName.Gt("m"))
                        .ToListAsync(static r => r.GetString(0));
                }
            }
            """;
        var generated = RunGenerator(JoinTables, site);
        Assert.DoesNotContain("InterceptsLocation", generated, StringComparison.Ordinal);
    }

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
        var email = new Mizzle.Ir.ColumnRef("u", "email", typeof(string));
        var p = new Mizzle.Ir.ParamRef(0, typeof(string));
        var ir = new Mizzle.Ir.SelectQuery(
            Select: [new Mizzle.Ir.SelectItem(email, null)],
            From: new Mizzle.Ir.FromSource("users", "public", "u"),
            Joins: [],
            Where: new Mizzle.Ir.BinaryExpr(Mizzle.Ir.BinaryOp.Eq, email, p),
            OrderBy: [], Limit: 10, Offset: null, Distinct: false,
            With: [], RecursiveWith: false, UnionAll: []);
        var runtime = new Mizzle.Postgres.PgEmitter().Emit(ir, ["x"]).Sql;
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
