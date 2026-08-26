namespace Mizzle.Generators.Tests;

public sealed class SchemaGeneratorTests
{
    [Fact]
    public void Generates_user_newuser_and_ordinal_mapper()
    {
        const string source = """
            using Mizzle.Postgres;

            namespace Demo;

            public sealed class Users : PgTable<Users>
            {
                public Users() : base("users", "public", "u") { }
                public PgColumn<int> Id { get; } = Identity("id");
                public PgColumn<string> Email { get; } = Text("email");
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(source));
        Assert.Contains("record User(", generated, StringComparison.Ordinal);
        Assert.Contains("GetInt32(0)", generated, StringComparison.Ordinal);
        Assert.Contains("record NewUser(string Email)", generated, StringComparison.Ordinal);
        Assert.Contains("GetString(1)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_MIZ001_when_pg_table_has_sql_column()
    {
        const string source = """
            using Mizzle.Postgres;
            using Mizzle.SqlServer;

            namespace Demo;

            public sealed class Users : PgTable<Users>
            {
                public Users() : base("users", "public", "u") { }
                public SqlColumn<string> Email { get; } = null!;
            }
            """;

        var result = GeneratorTestHost.Run(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "MIZ001");
    }
}
