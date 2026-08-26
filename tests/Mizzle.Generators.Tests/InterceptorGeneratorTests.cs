namespace Mizzle.Generators.Tests;

public sealed class InterceptorGeneratorTests
{
    [Fact]
    public void Fluent_ToListAsync_chain_emits_intercepts_location()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Data.Common;
            using System.Threading.Tasks;
            using Mizzle.Postgres;

            namespace Demo;

            public sealed class Users : PgTable<Users>
            {
                public Users() : base("users", "public", "u") { }
                public PgColumn<int> Id { get; } = Identity("id");
                public PgColumn<string> Email { get; } = Text("email");
            }

            public static class Queries
            {
                public static Task<IReadOnlyList<string>> List(PostgresDb db)
                {
                    var users = new Users();
                    return db.Select(users.Email)
                        .From(users.ToFrom())
                        .Where(users.Email, "a@b.com")
                        .ToListAsync(static r => r.GetString(0));
                }
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(source));
        Assert.Contains("InterceptsLocation", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Dynamic_SelectBuilder_is_not_intercepted()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Data.Common;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public sealed class Users : PgTable<Users>
            {
                public Users() : base("users", "public", "u") { }
                public PgColumn<string> Email { get; } = Text("email");
            }

            public static class Queries
            {
                public static Task<IReadOnlyList<string>> List(SelectBuilder builder)
                {
                    return builder.ToListAsync(static r => r.GetString(0));
                }
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(source));
        Assert.DoesNotContain("InterceptsLocation", generated, StringComparison.Ordinal);
    }
}
