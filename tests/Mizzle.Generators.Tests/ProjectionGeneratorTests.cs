using Microsoft.CodeAnalysis;

namespace Mizzle.Generators.Tests;

public sealed class ProjectionGeneratorTests
{
    private const string Tables = """
        using Mizzle.Postgres;

        namespace Demo;

        public sealed class Persons : PgTable<Persons>
        {
            public Persons() : base("person", "public", "a") { }
            public PgColumn<System.Guid> PersonId { get; } = Uuid("person_id").PrimaryKey();
            public PgColumn<System.Guid> LanguageId { get; } = Uuid("language_id");
            public PgColumn<string> FirstName { get; } = Text("first_name").NotNull();
        }

        public sealed class MstrLists : PgTable<MstrLists>
        {
            public MstrLists() : base("mstr_lists", "public", "c") { }
            public PgColumn<System.Guid> ItemId { get; } = Uuid("mstr_list_item_id").PrimaryKey();
            public PgColumn<string> ItemDesc { get; } = Text("mstr_list_item_desc").NotNull();
            public PgColumn<string> ListType { get; } = Text("mstr_list_type").NotNull();
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
                var a = new Persons();
                var c = new MstrLists();
                var rows = await db.Select(a.PersonId, a.FirstName, c.ItemDesc)
                    .From(a)
                    .LeftJoin(c).On(a.LanguageId.Eq(c.ItemId), c.ListType.Eq("language"))
                    .Where(a.PersonId.Eq(id))
                    .ToListAsync<PatientProfileRow>();
            }
        }
        """;

    [Fact]
    public void Generate_mode_declares_record_with_left_join_nullability()
    {
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(Tables, GenerateModeCallSite));
        Assert.Contains("public sealed record PatientProfileRow(", generated, StringComparison.Ordinal);
        Assert.Contains("global::System.Guid PersonId", generated, StringComparison.Ordinal);
        Assert.Contains("string FirstName", generated, StringComparison.Ordinal);
        // ItemDesc is NotNull on its table, but the LeftJoin makes it nullable.
        Assert.Contains("string? ItemDesc", generated, StringComparison.Ordinal);
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

    private static string MapModeCallSite(string typeName, string select = "a.PersonId, a.FirstName, c.ItemDesc") => $$"""
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.Postgres;

        namespace Demo;

        public static class MapQ
        {
            public static async Task Run(PostgresDb db, System.Guid id)
            {
                var a = new Persons();
                var c = new MstrLists();
                var rows = await db.Select({{select}})
                    .From(a)
                    .LeftJoin(c).On(a.LanguageId.Eq(c.ItemId), c.ListType.Eq("language"))
                    .Where(a.PersonId.Eq(id))
                    .ToListAsync<{{typeName}}>();
            }
        }
        """;

    [Fact]
    public void Map_mode_into_snake_case_poco()
    {
        const string poco = """
            namespace Demo;

            public sealed class ProfileRow
            {
                public System.Guid person_id { get; set; }
                public string first_name { get; set; } = "";
                public string? item_desc { get; set; }
            }
            """;
        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, poco, MapModeCallSite("ProfileRow"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("MIZ", StringComparison.Ordinal));
        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains("person_id = r.GetGuid(0)", generated, StringComparison.Ordinal);
        Assert.Contains("item_desc = r.IsDBNull(2) ? (string?)null : r.GetString(2)", generated, StringComparison.Ordinal);
        Assert.Contains("InterceptsLocation", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Map_mode_into_positional_record()
    {
        const string record = """
            namespace Demo;

            public sealed record Slim(System.Guid PersonId, string? ItemDesc);
            """;
        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(
            Tables, record, MapModeCallSite("Slim", "a.PersonId, c.ItemDesc"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("MIZ", StringComparison.Ordinal));
        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains("PersonId: r.GetGuid(0)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Map_mode_reports_MIZ003_for_unmatched_column()
    {
        const string target = """
            namespace Demo;

            public sealed class Sparse
            {
                public System.Guid person_id { get; set; }
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

            public sealed record Demanding(System.Guid PersonId, string FirstName, string? ItemDesc, string Missing);
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
                public System.Guid person_id { get; set; }
                public string first_name { get; set; } = "";
                public string item_desc { get; set; } = "";
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
                public System.Guid person_id { get; set; }
                public string? first_name { get; set; }
                public string? ItemDesc { get; set; }
                public string? item_desc { get; set; }
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
                    var a = new Persons();
                    _ = await db.Select(a.PersonId, a.FirstName).From(a).Where(a.PersonId.Eq(id)).ToListAsync<R1>();
                    _ = await db.Select(a.PersonId, a.FirstName).From(a).Where(a.PersonId.Eq(id)).FirstAsync<R1>();
                    _ = await db.Select(a.PersonId, a.FirstName).From(a).Where(a.PersonId.Eq(id)).FirstOrDefaultAsync<R1>();
                    _ = await db.Select(a.PersonId, a.FirstName).From(a).Where(a.PersonId.Eq(id)).SingleAsync<R1>();
                    _ = await db.Select(a.PersonId, a.FirstName).From(a).Where(a.PersonId.Eq(id)).SingleOrDefaultAsync<R1>();
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
}
