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
                public Users() : base("users", "public") { }
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

            public sealed class Documents : SqlTable<Documents>
            {
                public Documents() : base("documents") { }
                public SqlColumn<System.Guid> DocumentId { get; } = UniqueIdentifier("document_id").PrimaryKey();
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
                public Events() : base("events", "public") { }
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

            public sealed class Books : SqlTable<Books>
            {
                public Books() : base("books") { }
                public SqlColumn<System.Guid> BookId { get; } = UniqueIdentifier("book_id").PrimaryKey();
                public SqlColumn<string> Subtitle { get; } = NVarCharMax("subtitle");
                public SqlColumn<System.Guid> PublisherId { get; } = UniqueIdentifier("publisher_id");
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(source));
        // Nullable record members for unannotated columns
        Assert.Contains("string? Subtitle", generated, StringComparison.Ordinal);
        Assert.Contains("System.Guid? PublisherId", generated, StringComparison.Ordinal);
        // Non-nullable for PrimaryKey
        Assert.Contains("System.Guid BookId", generated, StringComparison.Ordinal);
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
                public Users() : base("users", "public") { }
                public PgColumn<int> Id { get; } = Identity("id").PrimaryKey();
                public PgColumn<string> Email { get; } = Text("email").NotNull();
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(source));
        Assert.Contains("record NewUser(string Email)", generated, StringComparison.Ordinal);
    }

    private const string ConverterSource = """
        namespace Demo;

        public static class EhrConvert
        {
            public static System.Guid ToGuid(string value) => System.Guid.Parse(value);
            public static string FromGuid(System.Guid value) => value.ToString("D");
            public static System.DateOnly ToDate(string value) => System.DateOnly.ParseExact(value, "yyyyMMdd");
            public static string FromDate(System.DateOnly value) => value.ToString("yyyyMMdd");
        }
        """;

    [Fact]
    public void Mapped_columns_generate_domain_types_and_converter_reads()
    {
        const string source = """
            using Mizzle.SqlServer;

            namespace Demo;

            public sealed class LegacyPersons : SqlTable<LegacyPersons>
            {
                public LegacyPersons() : base("person", "dbo") { }
                public SqlColumn<System.Guid> PersonId { get; } = Char("person_id", 36).Map(EhrConvert.ToGuid, EhrConvert.FromGuid).PrimaryKey();
                public SqlColumn<System.DateOnly> DateOfBirth { get; } = Char("date_of_birth", 8).Map(EhrConvert.ToDate, EhrConvert.FromDate);
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(ConverterSource, source));
        // Domain-typed record members
        Assert.Contains("global::System.Guid PersonId", generated, StringComparison.Ordinal);
        Assert.Contains("global::System.DateOnly? DateOfBirth", generated, StringComparison.Ordinal);
        // Converter wrapped around the storage reader
        Assert.Contains("global::Demo.EhrConvert.ToGuid(r.GetString(0))", generated, StringComparison.Ordinal);
        Assert.Contains(
            "r.IsDBNull(1) ? (global::System.DateOnly?)null : global::Demo.EhrConvert.ToDate(r.GetString(1))",
            generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Lambda_converter_reports_MIZ008()
    {
        const string source = """
            using Mizzle.SqlServer;

            namespace Demo;

            public sealed class LegacyPersons : SqlTable<LegacyPersons>
            {
                public LegacyPersons() : base("person", "dbo") { }
                public SqlColumn<System.Guid> PersonId { get; } = Char("person_id", 36).Map(s => System.Guid.Parse(s), g => g.ToString());
            }
            """;

        var result = GeneratorTestHost.Run(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "MIZ008");
    }

    [Fact]
    public void Nullable_converter_result_reports_MIZ009()
    {
        const string source = """
            using Mizzle.SqlServer;

            namespace Demo;

            public static class NullableConvert
            {
                public static System.Guid? ToGuid(string value) => System.Guid.Parse(value);
                public static string FromGuid(System.Guid? value) => value?.ToString() ?? "";
            }

            public sealed class LegacyPersons : SqlTable<LegacyPersons>
            {
                public LegacyPersons() : base("person", "dbo") { }
                public SqlColumn<System.Guid?> PersonId { get; } = Char("person_id", 36).Map(NullableConvert.ToGuid, NullableConvert.FromGuid);
            }
            """;

        var result = GeneratorTestHost.Run(source);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "MIZ009");
        var message = diagnostic.GetMessage();
        Assert.Contains("PersonId", message, StringComparison.Ordinal);
        Assert.Contains("System.Guid?", message, StringComparison.Ordinal);
        Assert.Contains("System.Guid'", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_converter_result_emits_no_double_nullable()
    {
        const string source = """
            using Mizzle.SqlServer;

            namespace Demo;

            public static class NullableConvert
            {
                public static System.Guid? ToGuid(string value) => System.Guid.Parse(value);
                public static string FromGuid(System.Guid? value) => value?.ToString() ?? "";
            }

            public sealed class LegacyPersons : SqlTable<LegacyPersons>
            {
                public LegacyPersons() : base("person", "dbo") { }
                public SqlColumn<System.Guid?> PersonId { get; } = Char("person_id", 36).Map(NullableConvert.ToGuid, NullableConvert.FromGuid);
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(source));
        Assert.DoesNotContain("??", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_reference_converter_result_reports_MIZ009()
    {
        const string source = """
            #nullable enable
            using Mizzle.SqlServer;

            namespace Demo;

            public static class NullableConvert
            {
                public static string? ToLabel(string value) => value;
                public static string FromLabel(string? value) => value ?? "";
            }

            public sealed class LegacyPersons : SqlTable<LegacyPersons>
            {
                public LegacyPersons() : base("person", "dbo") { }
                public SqlColumn<string?> Label { get; } = Char("label", 36).Map(NullableConvert.ToLabel, NullableConvert.FromLabel);
            }
            """;

        var result = GeneratorTestHost.Run(source);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "MIZ009");
        Assert.Contains("Label", diagnostic.GetMessage(), StringComparison.Ordinal);
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
                public Users() : base("users", "public") { }
                public SqlColumn<string> Email { get; } = null!;
            }
            """;

        var result = GeneratorTestHost.Run(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "MIZ001");
    }

    [Fact]
    public void Schema_mapper_trims_string_reads_when_flag_is_on()
    {
        const string source = """
            using System;
            using Mizzle.SqlServer;

            namespace Demo;

            public sealed class Paddeds : SqlTable<Paddeds>
            {
                public Paddeds() : base("padded", "dbo") { }
                public SqlColumn<Guid> PaddedId { get; } = UniqueIdentifier("padded_id").NotNull();
                public SqlColumn<string> City { get; } = VarChar("city", 35);
                public SqlColumn<string> Signature { get; } = VarChar("signature", 500).Untrimmed();
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(true, source));
        Assert.Contains("r.GetString(1).Trim()", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("r.GetString(2).Trim()", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("r.GetGuid(0).Trim()", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_mapper_leaves_reads_unchanged_when_flag_is_off()
    {
        const string source = """
            using Mizzle.SqlServer;

            namespace Demo;

            public sealed class Paddeds : SqlTable<Paddeds>
            {
                public Paddeds() : base("padded", "dbo") { }
                public SqlColumn<string> City { get; } = VarChar("city", 35);
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(false, source));
        Assert.DoesNotContain(".Trim()", generated, StringComparison.Ordinal);
    }

    [Theory]
    // Regression: naive "strip trailing s" produced Addres / Statu / Diagnosi.
    [InlineData("Address", "Address")]
    [InlineData("Status", "Status")]
    [InlineData("PatientDiagnosis", "PatientDiagnosis")]
    [InlineData("Business", "Business")]
    // Genuine plurals still singularize.
    [InlineData("Persons", "Person")]
    [InlineData("MstrLists", "MstrList")]
    [InlineData("Categories", "Category")]
    [InlineData("Addresses", "Address")]
    [InlineData("Statuses", "Status")]
    // Already singular, no trailing s.
    [InlineData("Person", "Person")]
    public void Record_name_singularizes_without_mangling(string tableClass, string expectedRecord)
    {
        var source = $$"""
            using Mizzle.SqlServer;

            namespace Demo;

            public sealed class {{tableClass}} : SqlTable<{{tableClass}}>
            {
                public {{tableClass}}() : base("t", "dbo", "x") { }
                public SqlColumn<string> Name { get; } = VarChar("name", 10).NotNull();
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(source));
        Assert.Contains($"public sealed record {expectedRecord}(", generated, StringComparison.Ordinal);
    }
}
