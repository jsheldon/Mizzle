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
}
