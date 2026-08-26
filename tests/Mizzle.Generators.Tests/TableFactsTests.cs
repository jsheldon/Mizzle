namespace Mizzle.Generators.Tests;

public sealed class TableFactsTests
{
    private static Microsoft.CodeAnalysis.INamedTypeSymbol GetSymbol(string source, string typeName)
    {
        var compilation = GeneratorTestHost.Compile(source);
        return compilation.GetTypeByMetadataName(typeName)
            ?? throw new InvalidOperationException($"Type {typeName} not found.");
    }

    [Fact]
    public void Extracts_table_and_column_facts()
    {
        const string source = """
            using Mizzle.Postgres;
            public sealed class Users : PgTable<Users>
            {
                public Users() : base("users", "public", "u") { }
                public PgColumn<int> Id { get; } = Identity("id").PrimaryKey();
                public PgColumn<string> Email { get; } = Text("email").NotNull();
            }
            """;
        var facts = TableFacts.FromSymbol(GetSymbol(source, "Users"));
        Assert.NotNull(facts);
        Assert.Equal("users", facts!.TableName);
        Assert.Equal("public", facts.Schema);
        Assert.Equal("u", facts.Alias);
        Assert.True(facts.IsPostgres);
        Assert.Collection(
            facts.Columns,
            c =>
            {
                Assert.Equal("Id", c.PropertyName);
                Assert.Equal("id", c.DbName);
                Assert.Equal("int", c.ClrTypeName);
            },
            c =>
            {
                Assert.Equal("Email", c.PropertyName);
                Assert.Equal("email", c.DbName);
                Assert.Equal("string", c.ClrTypeName);
            });
    }

    [Fact]
    public void Defaults_alias_to_table_name_when_absent()
    {
        const string source = """
            using Mizzle.Postgres;
            public sealed class Users : PgTable<Users>
            {
                public Users() : base("users") { }
                public PgColumn<string> Email { get; } = Text("email");
            }
            """;
        var facts = TableFacts.FromSymbol(GetSymbol(source, "Users"));
        Assert.NotNull(facts);
        Assert.Null(facts!.Schema);
        Assert.Equal("users", facts.Alias);
    }

    [Fact]
    public void Non_literal_ctor_arg_returns_null()
    {
        const string source = """
            using Mizzle.Postgres;
            public sealed class Users : PgTable<Users>
            {
                private static string N => "users";
                public Users() : base(N) { }
                public PgColumn<string> Email { get; } = Text("email");
            }
            """;
        Assert.Null(TableFacts.FromSymbol(GetSymbol(source, "Users")));
    }

    [Fact]
    public void Sql_server_table_is_not_postgres()
    {
        const string source = """
            using Mizzle.SqlServer;
            public sealed class Users : SqlTable<Users>
            {
                public Users() : base("users", "dbo", "u") { }
                public SqlColumn<string> Email { get; } = NVarChar("email", 255);
            }
            """;
        var facts = TableFacts.FromSymbol(GetSymbol(source, "Users"));
        Assert.NotNull(facts);
        Assert.False(facts!.IsPostgres);
        Assert.Equal("email", facts.Columns[0].DbName);
    }
}
