namespace Mizzle.Generators.Tests;

public sealed class InterceptorGeneratorTests
{
    private const string UsersTable = """
        using Mizzle.Postgres;

        namespace Demo;

        public sealed class Users : PgTable<Users>
        {
            public Users() : base("users", "public") { }
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

        public sealed class Authors : PgTable<Authors>
        {
            public Authors() : base("authors", "public") { }
            public PgColumn<System.Guid> AuthorId { get; } = Uuid("author_id").PrimaryKey();
            public PgColumn<System.Guid> FavoriteTagId { get; } = Uuid("favorite_tag_id");
            public PgColumn<string> DisplayName { get; } = Text("display_name").NotNull();
            public PgColumn<System.Guid> BlogId { get; } = Uuid("blog_id").NotNull();
        }

        public sealed class Tags : PgTable<Tags>
        {
            public Tags() : base("tags", "public") { }
            public PgColumn<System.Guid> TagId { get; } = Uuid("tag_id").PrimaryKey();
            public PgColumn<string> Label { get; } = Text("label");
            public PgColumn<string> Kind { get; } = Text("kind").NotNull();
        }
        """;

    private const string JoinedCallSite = """
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.Postgres;

        namespace Demo;

        public static class JoinedQ
        {
            public static async Task Run(PostgresDb db, System.Guid id, System.Guid blog)
            {
                var a = new Authors();
                var t = new Tags();
                _ = await db.Select(a.DisplayName, t.Label)
                    .From(a)
                    .LeftJoin(t).On(a.FavoriteTagId.Eq(t.TagId), t.Kind.Eq("topic"))
                    .Where(a.AuthorId.Eq(id))
                    .Where(a.BlogId.Eq(blog))
                    .ToListAsync(static r => r.GetString(0));
            }
        }
        """;

    private static string JoinedRuntimeSql()
    {
        var authorId = new Mizzle.Ir.ColumnRef("authors", "author_id", typeof(Guid));
        var blogId = new Mizzle.Ir.ColumnRef("authors", "blog_id", typeof(Guid));
        var ir = new Mizzle.Ir.SelectQuery(
            Select:
            [
                new Mizzle.Ir.SelectItem(new Mizzle.Ir.ColumnRef("authors", "display_name", typeof(string)), null),
                new Mizzle.Ir.SelectItem(new Mizzle.Ir.ColumnRef("tags", "label", typeof(string)), null)
            ],
            From: new Mizzle.Ir.FromSource("authors", "public", "authors"),
            Joins:
            [
                new Mizzle.Ir.JoinClause(
                    Mizzle.Ir.JoinKind.Left,
                    new Mizzle.Ir.FromSource("tags", "public", "tags"),
                    new Mizzle.Ir.BinaryExpr(
                        Mizzle.Ir.BinaryOp.And,
                        new Mizzle.Ir.BinaryExpr(
                            Mizzle.Ir.BinaryOp.Eq,
                            new Mizzle.Ir.ColumnRef("authors", "favorite_tag_id", typeof(Guid)),
                            new Mizzle.Ir.ColumnRef("tags", "tag_id", typeof(Guid))),
                        new Mizzle.Ir.BinaryExpr(
                            Mizzle.Ir.BinaryOp.Eq,
                            new Mizzle.Ir.ColumnRef("tags", "kind", typeof(string)),
                            new Mizzle.Ir.ValueExpr("topic", typeof(string)))))
            ],
            Where: new Mizzle.Ir.BinaryExpr(
                Mizzle.Ir.BinaryOp.And,
                new Mizzle.Ir.BinaryExpr(Mizzle.Ir.BinaryOp.Eq, authorId, new Mizzle.Ir.ValueExpr(Guid.Empty, typeof(Guid))),
                new Mizzle.Ir.BinaryExpr(Mizzle.Ir.BinaryOp.Eq, blogId, new Mizzle.Ir.ValueExpr(Guid.Empty, typeof(Guid)))),
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
                    var a = new Authors();
                    _ = await db.Select(a.DisplayName).From(a).Where(a.DisplayName.Gt("m"))
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
            "SELECT \\\"users\\\".\\\"email\\\" FROM \\\"public\\\".\\\"users\\\" AS \\\"users\\\" WHERE \\\"users\\\".\\\"email\\\" = $1 LIMIT 10",
            generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Baked_sql_matches_runtime_emitter()
    {
        var email = new Mizzle.Ir.ColumnRef("users", "email", typeof(string));
        var p = new Mizzle.Ir.ParamRef(0, typeof(string));
        var ir = new Mizzle.Ir.SelectQuery(
            Select: [new Mizzle.Ir.SelectItem(email, null)],
            From: new Mizzle.Ir.FromSource("users", "public", "users"),
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
                public SqlUsers() : base("users", "dbo") { }
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
            "SELECT [users].[email] FROM [dbo].[users] AS [users] WHERE [users].[email] = @p0",
            generated, StringComparison.Ordinal);
    }
}
