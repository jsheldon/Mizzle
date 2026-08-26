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
                public PgColumn<string> Email { get; } = Text("email").NotNull();
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(source));
        Assert.Contains("record User(", generated, StringComparison.Ordinal);
        Assert.Contains("GetInt32(0)", generated, StringComparison.Ordinal);
        Assert.Contains("record NewUser(string Email)", generated, StringComparison.Ordinal);
        Assert.Contains("GetString(1)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Guid_column_uses_GetGuid_not_GetValue()
    {
        const string source = """
            using Mizzle.SqlServer;

            namespace Demo;

            public sealed class Persons : SqlTable<Persons>
            {
                public Persons() : base("person", alias: "a") { }
                public SqlColumn<System.Guid> PersonId { get; } = UniqueIdentifier("person_id").PrimaryKey();
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(source));
        Assert.Contains("GetGuid(0)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("GetValue(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Unmapped_clr_type_uses_typed_GetFieldValue()
    {
        const string source = """
            using Mizzle.Postgres;

            namespace Demo;

            public sealed class Events : PgTable<Events>
            {
                public Events() : base("events", "public", "e") { }
                public PgColumn<System.DateTimeOffset> CreatedAt { get; } = Timestamptz("created_at").NotNull();
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(source));
        Assert.Contains("GetFieldValue<global::System.DateTimeOffset>(0)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("GetValue(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Columns_without_notnull_generate_nullable_members_and_guarded_reads()
    {
        const string source = """
            using Mizzle.SqlServer;

            namespace Demo;

            public sealed class Persons : SqlTable<Persons>
            {
                public Persons() : base("person", alias: "a") { }
                public SqlColumn<System.Guid> PersonId { get; } = UniqueIdentifier("person_id").PrimaryKey();
                public SqlColumn<string> MiddleName { get; } = NVarCharMax("middle_name");
                public SqlColumn<System.Guid> LanguageId { get; } = UniqueIdentifier("language_id");
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(source));
        // Nullable record members for unannotated columns
        Assert.Contains("string? MiddleName", generated, StringComparison.Ordinal);
        Assert.Contains("System.Guid? LanguageId", generated, StringComparison.Ordinal);
        // Non-nullable for PrimaryKey
        Assert.Contains("System.Guid PersonId", generated, StringComparison.Ordinal);
        // Guarded reads for nullable columns, direct read for required
        Assert.Contains("r.IsDBNull(1) ? (string?)null : r.GetString(1)", generated, StringComparison.Ordinal);
        Assert.Contains("r.IsDBNull(2) ? (global::System.Guid?)null : r.GetGuid(2)", generated, StringComparison.Ordinal);
        Assert.Contains("r.GetGuid(0)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Chained_modifiers_still_classify_identity()
    {
        const string source = """
            using Mizzle.Postgres;

            namespace Demo;

            public sealed class Users : PgTable<Users>
            {
                public Users() : base("users", "public", "u") { }
                public PgColumn<int> Id { get; } = Identity("id").PrimaryKey();
                public PgColumn<string> Email { get; } = Text("email").NotNull();
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(source));
        Assert.Contains("record NewUser(string Email)", generated, StringComparison.Ordinal);
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
