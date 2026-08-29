namespace Mizzle.Generators.Tests;

public sealed class TableFactsTests
{
    private static (Microsoft.CodeAnalysis.INamedTypeSymbol Symbol, Microsoft.CodeAnalysis.Compilation Compilation) GetSymbol(string source, string typeName)
    {
        var compilation = GeneratorTestHost.Compile(source);
        var symbol = compilation.GetTypeByMetadataName(typeName)
            ?? throw new InvalidOperationException($"Type {typeName} not found.");
        return (symbol, compilation);
    }

    [Fact]
    public void Extracts_table_and_column_facts()
    {
        const string source = """
            using Mizzle.Postgres;
            public sealed class Users : PgTable<Users>
            {
                public Users() : base("users", "public") { }
                public PgColumn<int> Id { get; } = Identity("id").PrimaryKey();
                public PgColumn<string> Email { get; } = Text("email").NotNull();
            }
            """;
        var (symbol, compilation) = GetSymbol(source, "Users");
        var facts = TableFacts.FromSymbol(symbol, compilation);
        Assert.NotNull(facts);
        Assert.Equal("users", facts!.TableName);
        Assert.Equal("public", facts.Schema);
        Assert.Equal("users", facts.Alias);
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
        var (symbol, compilation) = GetSymbol(source, "Users");
        var facts = TableFacts.FromSymbol(symbol, compilation);
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
        var (symbol, compilation) = GetSymbol(source, "Users");
        Assert.Null(TableFacts.FromSymbol(symbol, compilation));
    }

    [Fact]
    public void AlwaysFilter_modifier_is_extracted()
    {
        const string source = """
            using Mizzle.Postgres;
            public sealed class Orders : PgTable<Orders>
            {
                public Orders() : base("orders") { }
                public PgColumn<int> Id { get; } = Identity("id").PrimaryKey();
                public PgColumn<int> TenantId { get; } = Integer("tenant_id").NotNull().AlwaysFilter();
            }
            """;
        var (symbol, compilation) = GetSymbol(source, "Orders");
        var facts = TableFacts.FromSymbol(symbol, compilation);
        Assert.NotNull(facts);
        Assert.False(facts!.Columns[0].IsAlwaysFilter);
        Assert.True(facts.Columns[1].IsAlwaysFilter);
    }

    [Fact]
    public void Sql_server_table_is_not_postgres()
    {
        const string source = """
            using Mizzle.SqlServer;
            public sealed class Users : SqlTable<Users>
            {
                public Users() : base("users", "dbo") { }
                public SqlColumn<string> Email { get; } = NVarChar("email", 255);
            }
            """;
        var (symbol, compilation) = GetSymbol(source, "Users");
        var facts = TableFacts.FromSymbol(symbol, compilation);
        Assert.NotNull(facts);
        Assert.False(facts!.IsPostgres);
        Assert.Equal("email", facts.Columns[0].DbName);
    }
}
