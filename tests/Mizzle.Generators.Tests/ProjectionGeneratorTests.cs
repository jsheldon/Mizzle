using Microsoft.CodeAnalysis;

namespace Mizzle.Generators.Tests;

public sealed class ProjectionGeneratorTests
{
    private const string Tables = """
        using Mizzle.Postgres;

        namespace Demo;

        public sealed class Authors : PgTable<Authors>
        {
            public Authors() : base("authors", "public", "a") { }
            public PgColumn<System.Guid> AuthorId { get; } = Uuid("author_id").PrimaryKey();
            public PgColumn<System.Guid> FavoriteTagId { get; } = Uuid("favorite_tag_id");
            public PgColumn<string> DisplayName { get; } = Text("display_name").NotNull();
        }

        public sealed class Tags : PgTable<Tags>
        {
            public Tags() : base("tags", "public", "t") { }
            public PgColumn<System.Guid> TagId { get; } = Uuid("tag_id").PrimaryKey();
            public PgColumn<string> Label { get; } = Text("label").NotNull();
            public PgColumn<string> Kind { get; } = Text("kind").NotNull();
        }
        """;

    private const string GenerateModeCallSite = """
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.Postgres;

        namespace Demo;

        public static class Q
        {
            public static async Task Run(PostgresDb db, System.Guid id)
            {
                var a = new Authors();
                var t = new Tags();
                var rows = await db.Select(a.AuthorId, a.DisplayName, t.Label)
                    .From(a)
                    .LeftJoin(t).On(a.FavoriteTagId.Eq(t.TagId), t.Kind.Eq("topic"))
                    .Where(a.AuthorId.Eq(id))
                    .ToListAsync<AuthorTagRow>();
            }
        }
        """;

    [Fact]
    public void Generate_mode_declares_record_with_left_join_nullability()
    {
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(Tables, GenerateModeCallSite));
        Assert.Contains("public sealed record AuthorTagRow(", generated, StringComparison.Ordinal);
        Assert.Contains("global::System.Guid AuthorId", generated, StringComparison.Ordinal);
        Assert.Contains("string DisplayName", generated, StringComparison.Ordinal);
        // Label is NotNull on its table, but the LeftJoin makes it nullable.
        Assert.Contains("string? Label", generated, StringComparison.Ordinal);
        Assert.Contains("InterceptsLocation", generated, StringComparison.Ordinal);
        Assert.Contains("ToListPrecompiledAsync", generated, StringComparison.Ordinal);
        Assert.Contains("r.GetGuid(0)", generated, StringComparison.Ordinal);
        Assert.Contains("r.IsDBNull(2) ? (string?)null : r.GetString(2)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_mode_output_compiles_cleanly()
    {
        var (_, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, GenerateModeCallSite);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    private static string MapModeCallSite(string typeName, string select = "a.AuthorId, a.DisplayName, t.Label") => $$"""
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.Postgres;

        namespace Demo;

        public static class MapQ
        {
            public static async Task Run(PostgresDb db, System.Guid id)
            {
                var a = new Authors();
                var t = new Tags();
                var rows = await db.Select({{select}})
                    .From(a)
                    .LeftJoin(t).On(a.FavoriteTagId.Eq(t.TagId), t.Kind.Eq("topic"))
                    .Where(a.AuthorId.Eq(id))
                    .ToListAsync<{{typeName}}>();
            }
        }
        """;

    [Fact]
    public void Map_mode_into_snake_case_poco()
    {
        const string poco = """
            namespace Demo;

            public sealed class AuthorRow
            {
                public System.Guid author_id { get; set; }
                public string display_name { get; set; } = "";
                public string? label { get; set; }
            }
            """;
        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, poco, MapModeCallSite("AuthorRow"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("MIZ", StringComparison.Ordinal));
        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains("author_id = r.GetGuid(0)", generated, StringComparison.Ordinal);
        Assert.Contains("label = r.IsDBNull(2) ? (string?)null : r.GetString(2)", generated, StringComparison.Ordinal);
        Assert.Contains("InterceptsLocation", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Map_mode_into_positional_record()
    {
        const string record = """
            namespace Demo;

            public sealed record Slim(System.Guid AuthorId, string? Label);
            """;
        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(
            Tables, record, MapModeCallSite("Slim", "a.AuthorId, t.Label"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("MIZ", StringComparison.Ordinal));
        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains("AuthorId: r.GetGuid(0)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Map_mode_reports_MIZ003_for_unmatched_column()
    {
        const string target = """
            namespace Demo;

            public sealed class Sparse
            {
                public System.Guid author_id { get; set; }
            }
            """;
        var result = GeneratorTestHost.Run(Tables, target, MapModeCallSite("Sparse"));
        Assert.Contains(result.Diagnostics, d => d.Id == "MIZ003");
    }

    [Fact]
    public void Map_mode_reports_MIZ004_for_unfilled_required_member()
    {
        const string target = """
            namespace Demo;

            public sealed record Demanding(System.Guid AuthorId, string DisplayName, string? Label, string Missing);
            """;
        var result = GeneratorTestHost.Run(Tables, target, MapModeCallSite("Demanding"));
        Assert.Contains(result.Diagnostics, d => d.Id == "MIZ004");
    }

    [Fact]
    public void Map_mode_reports_MIZ005_for_nullable_column_into_non_nullable_member()
    {
        const string target = """
            namespace Demo;

            public sealed class Strict
            {
                public System.Guid author_id { get; set; }
                public string display_name { get; set; } = "";
                public string label { get; set; } = "";
            }
            """;
        var result = GeneratorTestHost.Run(Tables, target, MapModeCallSite("Strict"));
        Assert.Contains(result.Diagnostics, d => d.Id == "MIZ005");
    }

    [Fact]
    public void Map_mode_reports_MIZ006_for_ambiguous_members()
    {
        const string target = """
            namespace Demo;

            public sealed class Ambiguous
            {
                public System.Guid author_id { get; set; }
                public string? display_name { get; set; }
                public string? Label { get; set; }
                public string? label { get; set; }
            }
            """;
        var result = GeneratorTestHost.Run(Tables, target, MapModeCallSite("Ambiguous"));
        Assert.Contains(result.Diagnostics, d => d.Id == "MIZ006");
    }

    [Fact]
    public void All_typed_terminators_get_interceptors()
    {
        const string site = """
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public static class TermQ
            {
                public static async Task Run(PostgresDb db, System.Guid id)
                {
                    var a = new Authors();
                    _ = await db.Select(a.AuthorId, a.DisplayName).From(a).Where(a.AuthorId.Eq(id)).ToListAsync<R1>();
                    _ = await db.Select(a.AuthorId, a.DisplayName).From(a).Where(a.AuthorId.Eq(id)).FirstAsync<R1>();
                    _ = await db.Select(a.AuthorId, a.DisplayName).From(a).Where(a.AuthorId.Eq(id)).FirstOrDefaultAsync<R1>();
                    _ = await db.Select(a.AuthorId, a.DisplayName).From(a).Where(a.AuthorId.Eq(id)).SingleAsync<R1>();
                    _ = await db.Select(a.AuthorId, a.DisplayName).From(a).Where(a.AuthorId.Eq(id)).SingleOrDefaultAsync<R1>();
                }
            }
            """;
        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, site);
        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains("ToListProjected", generated, StringComparison.Ordinal);
        Assert.Contains("FirstProjected", generated, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultProjected", generated, StringComparison.Ordinal);
        Assert.Contains("SingleProjected", generated, StringComparison.Ordinal);
        Assert.Contains("SingleOrDefaultProjected", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Projections_bake_converter_reads_for_mapped_columns()
    {
        const string converters = """
            namespace Demo;

            public static class EhrConvert
            {
                public static System.Guid ToGuid(string value) => System.Guid.Parse(value);
                public static string FromGuid(System.Guid value) => value.ToString("D");
            }
            """;
        const string table = """
            using Mizzle.SqlServer;

            namespace Demo;

            public sealed class LegacyPersons : SqlTable<LegacyPersons>
            {
                public LegacyPersons() : base("person", "dbo", "a") { }
                public SqlColumn<System.Guid> PersonId { get; } = Char("person_id", 36).Map(EhrConvert.ToGuid, EhrConvert.FromGuid).PrimaryKey();
                public SqlColumn<string> FirstName { get; } = VarChar("first_name", 50).NotNull();
            }
            """;
        const string site = """
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            public static class LegacyQ
            {
                public static async Task Run(SqlDb db, System.Guid id)
                {
                    var a = new LegacyPersons();
                    var rows = await db.Select(a.PersonId, a.FirstName)
                        .From(a)
                        .Where(a.PersonId.Eq(id))
                        .ToListAsync<LegacyRow>();
                }
            }
            """;
        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(converters, table, site);
        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains("global::System.Guid PersonId", generated, StringComparison.Ordinal);
        Assert.Contains("global::Demo.EhrConvert.ToGuid(r.GetString(0))", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Unbound_type_on_dynamic_chain_reports_MIZ007()
    {
        const string site = """
            using System.Threading.Tasks;
            using Mizzle.Fluent;

            namespace Demo;

            public static class Q
            {
                public static Task Run(SelectBuilder prebuilt)
                    => prebuilt.ToListAsync<MysteryRow>();
            }
            """;
        var result = GeneratorTestHost.Run(Tables, site);
        Assert.Contains(result.Diagnostics, d => d.Id == "MIZ007");
    }

    [Fact]
    public void Column_error_suppresses_follow_on_MIZ007()
    {
        const string tables = """
            using Mizzle.Postgres;

            namespace Demo;

            public static class NullableConvert
            {
                public static System.Guid? ToGuid(string value) => System.Guid.Parse(value);
                public static string FromGuid(System.Guid? value) => value?.ToString() ?? "";
            }

            public sealed class Widgets : PgTable<Widgets>
            {
                public Widgets() : base("widgets", "public", "w") { }
                public PgColumn<System.Guid?> WidgetId { get; } = Text("widget_id").Map(NullableConvert.ToGuid, NullableConvert.FromGuid);
            }
            """;
        const string callSite = """
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public static class Q
            {
                public static async Task Run(PostgresDb db)
                {
                    var w = new Widgets();
                    var rows = await db.Select(w.WidgetId).From(w).ToListAsync<WidgetRow>();
                }
            }
            """;

        var result = GeneratorTestHost.Run(tables, callSite);
        // MIZ009 points at the real line; MIZ007 would only add noise.
        Assert.Contains(result.Diagnostics, d => d.Id == "MIZ009");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "MIZ007");
    }

    private const string ConvertedTables = """
        using System;
        using Mizzle.SqlServer;

        namespace Demo;

        internal static class EhrConvert
        {
            public static DateOnly ToDateOnly(string value) => DateOnly.ParseExact(value, "yyyyMMdd");
            public static string FromDateOnly(DateOnly value) => value.ToString("yyyyMMdd");
        }

        public sealed class Persons : SqlTable<Persons>
        {
            public Persons() : base("person", "dbo", "a") { }
            public SqlColumn<Guid> PersonId { get; } = UniqueIdentifier("person_id").NotNull();
            public SqlColumn<int> VisitCount { get; } = Int("visit_count").NotNull();
            public SqlColumn<DateOnly> DateOfBirth { get; } = VarChar("date_of_birth", 8).Map(EhrConvert.ToDateOnly, EhrConvert.FromDateOnly);
        }
        """;

    private static string ConvertedCallSite(string dobType, string countType) => $$"""
        using System;
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.SqlServer;

        namespace Demo;

        public class PersonRow
        {
            public Guid person_id { get; set; }
            public {{countType}} visit_count { get; set; }
            public {{dobType}} date_of_birth { get; set; }
        }

        public static class Q
        {
            public static async Task Run(SqlDb db)
            {
                var p = new Persons();
                var rows = await db.Select(p.PersonId, p.VisitCount, p.DateOfBirth)
                    .From(p)
                    .ToListAsync<PersonRow>();
            }
        }
        """;

    [Fact]
    public void Converted_column_into_wrong_member_type_reports_MIZ010()
    {
        var result = GeneratorTestHost.Run(ConvertedTables, ConvertedCallSite("string?", "int"));
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "MIZ010");
        var message = diagnostic.GetMessage();
        Assert.Contains("DateOfBirth", message, StringComparison.Ordinal);
        Assert.Contains("DateOnly", message, StringComparison.Ordinal);
        Assert.Contains("date_of_birth", message, StringComparison.Ordinal);
        // Reported at the user's call site, not inside the generated file.
        var span = diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);
        Assert.Contains("ToListAsync<PersonRow>", span, StringComparison.Ordinal);
    }

    [Fact]
    public void Converted_column_into_wrong_member_type_emits_no_broken_mapper()
    {
        var (_, diagnostics) = GeneratorTestHost.RunAndCompile(ConvertedTables, ConvertedCallSite("string?", "int"));
        // Without MIZ010 this surfaced as CS0029 inside Mizzle.Projections.g.cs.
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0029");
    }

    [Fact]
    public void Matching_and_widening_member_types_do_not_report_MIZ010()
    {
        // DateOnly -> DateOnly exactly; int -> long by implicit widening.
        var result = GeneratorTestHost.Run(ConvertedTables, ConvertedCallSite("DateOnly?", "long"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "MIZ010");
    }

    private const string AliasTables = """
        using Mizzle.Postgres;

        namespace Demo;

        public sealed class Persons : PgTable<Persons>
        {
            public Persons() : base("persons", "public", "p") { }
            public PgColumn<System.Guid> PersonId { get; } = Uuid("person_id").PrimaryKey();
            public PgColumn<string> Zip { get; } = Text("zip").NotNull();
        }
        """;

    private const string AliasCallSite = """
        using System;
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.Postgres;

        namespace Demo;

        public class PersonRow
        {
            public Guid PatientId { get; set; }
            public string PostalCode { get; set; } = "";
        }

        public static class AliasQ
        {
            public static async Task Run(PostgresDb db)
            {
                var p = new Persons();
                var rows = await db.Select(p.PersonId.As("PatientId"), p.Zip.As("PostalCode"))
                    .From(p)
                    .ToListAsync<PersonRow>();
            }
        }
        """;

    [Fact]
    public void Alias_binds_column_to_renamed_member()
    {
        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(AliasTables, AliasCallSite);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.Diagnostics);
        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains("PatientId = r.GetGuid(0)", generated, StringComparison.Ordinal);
        Assert.Contains("PostalCode = r.GetString(1)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Alias_emits_sql_as_clause()
    {
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(AliasTables, AliasCallSite));
        Assert.Contains(
            "SELECT \\\"p\\\".\\\"person_id\\\" AS \\\"PatientId\\\", \\\"p\\\".\\\"zip\\\" AS \\\"PostalCode\\\"",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Alias_names_the_generated_record_member()
    {
        const string callSite = """
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public static class GenAliasQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var p = new Persons();
                    var rows = await db.Select(p.Zip.As("PostalCode")).From(p).ToListAsync<ZipRow>();
                }
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(AliasTables, callSite));
        Assert.Contains("public sealed record ZipRow(string PostalCode)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_literal_alias_falls_back_to_runtime()
    {
        const string callSite = """
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public class DynRow
            {
                public string PostalCode { get; set; } = "";
            }

            public static class DynAliasQ
            {
                public static string Name = "PostalCode";

                public static async Task Run(PostgresDb db)
                {
                    var p = new Persons();
                    var rows = await db.Select(p.Zip.As(Name)).From(p).ToListAsync<DynRow>();
                }
            }
            """;

        var result = GeneratorTestHost.Run(AliasTables, callSite);
        // Bound T on an unbakeable chain: silent fallback, no interceptor.
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain("DynRowIntoMapper", GeneratorTestHost.Generated(result), StringComparison.Ordinal);
    }

    private static string AliasDiagnosticCallSite(string memberType, string aliasName) => $$"""
        using System;
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.Postgres;

        namespace Demo;

        public class DiagRow
        {
            public Guid PatientId { get; set; }
            public {{memberType}} PostalCode { get; set; }
        }

        public static class DiagQ
        {
            public static async Task Run(PostgresDb db)
            {
                var p = new Persons();
                var rows = await db.Select(p.PersonId.As("PatientId"), p.Zip.As("{{aliasName}}"))
                    .From(p)
                    .ToListAsync<DiagRow>();
            }
        }
        """;

    [Fact]
    public void Alias_naming_a_missing_member_reports_MIZ003()
    {
        var result = GeneratorTestHost.Run(AliasTables, AliasDiagnosticCallSite("string", "Postcode"));
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "MIZ003");
        // The message must name what the user wrote, not the schema property.
        Assert.Contains("Postcode", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain("'Zip'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Alias_onto_a_wrongly_typed_member_reports_MIZ010()
    {
        var result = GeneratorTestHost.Run(AliasTables, AliasDiagnosticCallSite("int", "PostalCode"));
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "MIZ010");
        Assert.Contains("PostalCode", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_alias_reports_MIZ003_for_the_second_column()
    {
        const string callSite = """
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public class DupRow
            {
                public string PostalCode { get; set; } = "";
            }

            public static class DupQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var p = new Persons();
                    var rows = await db.Select(p.Zip.As("PostalCode"), p.Zip.As("PostalCode"))
                        .From(p)
                        .ToListAsync<DupRow>();
                }
            }
            """;

        var result = GeneratorTestHost.Run(AliasTables, callSite);
        Assert.Contains(result.Diagnostics, d => d.Id == "MIZ003");
    }

    [Fact]
    public void Alias_in_a_where_clause_is_ignored()
    {
        const string callSite = """
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public class WhereRow
            {
                public string PostalCode { get; set; } = "";
            }

            public static class WhereAliasQ
            {
                public static async Task Run(PostgresDb db, Guid id)
                {
                    var p = new Persons();
                    var rows = await db.Select(p.Zip.As("PostalCode"))
                        .From(p)
                        .Where(p.PersonId.As("Ignored").Eq(id))
                        .ToListAsync<WhereRow>();
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(AliasTables, callSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var generated = GeneratorTestHost.Generated(result);
        // The WHERE predicate is unaffected by the alias.
        Assert.Contains("WHERE \\\"p\\\".\\\"person_id\\\" = $1", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("AS \\\"Ignored\\\"", generated, StringComparison.Ordinal);
    }

    private const string TrimTables = """
        using System;
        using Mizzle.SqlServer;

        namespace Demo;

        internal static class TrimConvert
        {
            public static bool ToBoolean(string value) => value == "Y";
            public static string ToIndicator(bool value) => value ? "Y" : "N";
        }

        public sealed class Paddeds : SqlTable<Paddeds>
        {
            public Paddeds() : base("padded", "dbo", "d") { }
            public SqlColumn<Guid> PaddedId { get; } = UniqueIdentifier("padded_id").NotNull();
            public SqlColumn<string> City { get; } = VarChar("city", 35);
            public SqlColumn<string> Signature { get; } = VarChar("signature", 500).Untrimmed();
            public SqlColumn<bool> Flag { get; } = Char("flag", 1).Map(TrimConvert.ToBoolean, TrimConvert.ToIndicator);
        }
        """;

    private const string TrimCallSite = """
        using System;
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.SqlServer;

        namespace Demo;

        public class PaddedRow
        {
            public Guid PaddedId { get; set; }
            public string? City { get; set; }
            public string? Signature { get; set; }
            public bool? Flag { get; set; }
        }

        public static class TrimQ
        {
            public static async Task Run(SqlDb db)
            {
                var d = new Paddeds();
                var rows = await db.Select(d.PaddedId, d.City, d.Signature, d.Flag)
                    .From(d)
                    .ToListAsync<PaddedRow>();
            }
        }
        """;

    [Fact]
    public void Trim_flag_off_leaves_reads_unchanged()
    {
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(false, TrimTables, TrimCallSite));
        Assert.DoesNotContain(".Trim()", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Trim_flag_on_trims_string_reads_only()
    {
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(true, TrimTables, TrimCallSite));
        Assert.Contains("r.IsDBNull(1) ? (string?)null : r.GetString(1).Trim()", generated, StringComparison.Ordinal);
        Assert.Contains("r.GetGuid(0)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("r.GetGuid(0).Trim()", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Untrimmed_column_overrides_the_trim_flag()
    {
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(true, TrimTables, TrimCallSite));
        Assert.Contains("r.IsDBNull(2) ? (string?)null : r.GetString(2)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("r.GetString(2).Trim()", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Trim_applies_inside_a_map_converter()
    {
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(true, TrimTables, TrimCallSite));
        Assert.Contains(
            "global::Demo.TrimConvert.ToBoolean(r.GetString(3).Trim())",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Trimmed_output_compiles_cleanly()
    {
        var (_, diagnostics) = GeneratorTestHost.RunAndCompile(true, TrimTables, TrimCallSite);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Aliased_converted_trimmed_multi_join_maps_into_a_positional_record()
    {
        const string tables = """
            using System;
            using Mizzle.SqlServer;

            namespace Demo;

            internal static class E2EConvert
            {
                public static DateTime ToDateTime(string value) => DateTime.ParseExact(value, "yyyyMMdd", null);
                public static string FromDateTime(DateTime value) => value.ToString("yyyyMMdd");
                public static bool ToBoolean(string value) => value == "Y";
                public static string ToIndicator(bool value) => value ? "Y" : "N";
            }

            public sealed class EhrPersons : SqlTable<EhrPersons>
            {
                public EhrPersons() : base("person", "dbo", "a") { }
                public SqlColumn<Guid> PersonId { get; } = UniqueIdentifier("person_id").NotNull();
                public SqlColumn<string> Zip { get; } = VarChar("zip", 9);
                public SqlColumn<Guid> LanguageId { get; } = UniqueIdentifier("language_id");
                public SqlColumn<DateTime> DateOfBirth { get; } = VarChar("date_of_birth", 8).Map(E2EConvert.ToDateTime, E2EConvert.FromDateTime);
                public SqlColumn<bool> ExpiredInd { get; } = Char("expired_ind", 1).Map(E2EConvert.ToBoolean, E2EConvert.ToIndicator).NotNull();
            }

            public sealed class EhrLists : SqlTable<EhrLists>
            {
                public EhrLists() : base("mstr_lists", "dbo", "c") { }
                public SqlColumn<Guid> MstrListItemId { get; } = UniqueIdentifier("mstr_list_item_id");
                public SqlColumn<string> MstrListItemDesc { get; } = VarChar("mstr_list_item_desc", 50).NotNull();
            }
            """;
        const string callSite = """
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            public record ProfileData(
                Guid PatientId,
                string? PostalCode,
                Guid? LanguageId,
                string? LanguageDescription,
                DateTime? DateOfBirth,
                bool Expired);

            public static class E2EQ
            {
                public static async Task<ProfileData?> Run(SqlDb db, Guid id)
                {
                    var p = new EhrPersons();
                    var m = new EhrLists();
                    return await db.Select(
                            p.PersonId.As("PatientId"),
                            p.Zip.As("PostalCode"),
                            p.LanguageId,
                            m.MstrListItemDesc.As("LanguageDescription"),
                            p.DateOfBirth,
                            p.ExpiredInd.As("Expired"))
                        .From(p)
                        .LeftJoin(m).On(p.LanguageId.Eq(m.MstrListItemId))
                        .Where(p.PersonId.Eq(id))
                        .FirstOrDefaultAsync<ProfileData>(default);
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(true, tables, callSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generated = GeneratorTestHost.Generated(result);
        // Aliases reach the ctor by name.
        Assert.Contains("PatientId: r.GetGuid(0)", generated, StringComparison.Ordinal);
        // Trimming lands inside the converter.
        Assert.Contains("E2EConvert.ToDateTime(r.GetString(4).Trim())", generated, StringComparison.Ordinal);
        // NotNull() on a left-joined table is still demoted to nullable.
        Assert.Contains("LanguageDescription: r.IsDBNull(3)", generated, StringComparison.Ordinal);
        // NotNull() on the FROM table stays non-nullable.
        Assert.Contains("Expired: global::Demo.E2EConvert.ToBoolean(r.GetString(5).Trim())", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_target_in_another_namespace_maps_correctly()
    {
        const string callSite = """
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;
            using Contracts.Profiles;

            namespace Contracts.Profiles
            {
                public record ProfileContract(Guid PatientId, string PostalCode);
            }

            namespace Infra.Reads
            {
                using Demo;

                public static class CrossNsQ
                {
                    public static async Task Run(PostgresDb db)
                    {
                        var p = new Persons();
                        var rows = await db.Select(p.PersonId.As("PatientId"), p.Zip.As("PostalCode"))
                            .From(p)
                            .ToListAsync<ProfileContract>();
                    }
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(AliasTables, callSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        // The mapper's return type must be the target's real namespace, not the
        // call site's (Infra.Reads).
        Assert.Contains("global::Contracts.Profiles.ProfileContract Read(", GeneratorTestHost.Generated(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Named_alias_argument_is_not_read_as_schema()
    {
        const string tables = """
            using Mizzle.SqlServer;

            namespace Demo;

            public sealed class Folks : SqlTable<Folks>
            {
                // Named argument skips schema -- must not be read positionally.
                public Folks() : base("person", alias: "a") { }
                public SqlColumn<System.Guid> PersonId { get; } = UniqueIdentifier("person_id").NotNull();
            }
            """;
        const string callSite = """
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            public class FolkRow
            {
                public Guid PersonId { get; set; }
            }

            public static class FolkQ
            {
                public static async Task Run(SqlDb db)
                {
                    var f = new Folks();
                    var rows = await db.Select(f.PersonId).From(f).ToListAsync<FolkRow>();
                }
            }
            """;

        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(tables, callSite));
        Assert.Contains("FROM [person] AS [a]", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("[a].[person]", generated, StringComparison.Ordinal);
    }
}
