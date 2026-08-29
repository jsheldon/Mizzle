using Microsoft.CodeAnalysis.CSharp;
using Mizzle.Compile;
using Mizzle.Fluent;
using Mizzle.SqlServer;

namespace Mizzle.Generators.Tests;

file sealed class Vocab : SqlTable<Vocab>
{
    public Vocab() : base("revdel0", "dbo") { }
    public SqlColumn<string> VocabId { get; } = VarChar("vocab_id", 20).NotNull();
    public SqlColumn<string> Code { get; } = VarChar("code", 50);
    public SqlColumn<decimal> TypeId { get; } = Numeric("type_id").NotNull();
}

// IN and CASE are the two halves of the legacy "best RxNorm per NDC" lookup: IN
// picks the candidate set, CASE ranks it. Without both, the query has to be
// written as one UNION ALL branch per rank.
public sealed class InCaseBakeTests
{
    private const string Tables = """
        using Mizzle.SqlServer;

        namespace Demo;

        public sealed class Vocab : SqlTable<Vocab>
        {
            public Vocab() : base("revdel0", "dbo") { }
            public SqlColumn<string> VocabId { get; } = VarChar("vocab_id", 20).NotNull();
            public SqlColumn<string> Code { get; } = VarChar("code", 50);
            public SqlColumn<decimal> TypeId { get; } = Numeric("type_id").NotNull();
        }
        """;

    private static string RuntimeSql(SelectBuilder builder)
    {
        var (canonical, values) = Parameterizer.Run(builder.Build());
        return new SqlServerEmitter().Emit(canonical, values).Sql;
    }

    private static string Generated(string callSite)
        => GeneratorTestHost.Generated(GeneratorTestHost.Run(Tables, callSite));

    private static string BakedSql(string callSite)
    {
        var generated = Generated(callSite);
        const string open = "            var sql = ";
        var start = generated.IndexOf(open, StringComparison.Ordinal);
        Assert.True(start >= 0, "no baked SQL assignment found in generated output:\n" + generated);
        start += open.Length;
        var lineEnd = generated.IndexOf('\n', start);
        return generated.Substring(start, lineEnd - start).TrimEnd('\r').TrimEnd(';');
    }

    [Fact]
    public void In_bakes_the_same_sql_the_runtime_emits()
    {
        var v = new Vocab();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(v.VocabId)
            .From(v.ToFrom())
            .Where(v.TypeId.In(504m, 502m, 503m, 501m, 505m)));

        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.SqlServer;

            namespace Demo;

            public record InRow(string VocabId);

            public static class InQ
            {
                public static async Task Run(SqlDb db)
                {
                    var v = new Vocab();
                    var rows = await db.Select(v.VocabId)
                        .From(v)
                        .Where(v.TypeId.In(504m, 502m, 503m, 501m, 505m))
                        .ToListAsync<InRow>();
                }
            }
            """);

        Assert.Contains("[type_id] IN (@p0, @p1, @p2, @p3, @p4)", baked, StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    // The slot numbers depend on everything emitted before the list, so an IN
    // that is not first has to keep counting from where the earlier binds stopped.
    [Fact]
    public void In_after_another_predicate_numbers_its_binds_from_the_running_slot()
    {
        var v = new Vocab();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(v.VocabId)
            .From(v.ToFrom())
            .Where(v.Code.Eq("x"))
            .Where(v.TypeId.In(504m, 502m))
            .Where(v.VocabId.Eq("y")));

        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.SqlServer;

            namespace Demo;

            public record InRow(string VocabId);

            public static class InOrderQ
            {
                public static async Task Run(SqlDb db)
                {
                    var v = new Vocab();
                    var rows = await db.Select(v.VocabId)
                        .From(v)
                        .Where(v.Code.Eq("x"))
                        .Where(v.TypeId.In(504m, 502m))
                        .Where(v.VocabId.Eq("y"))
                        .ToListAsync<InRow>();
                }
            }
            """);

        Assert.Contains("[type_id] IN (@p1, @p2)", baked, StringComparison.Ordinal);
        Assert.Contains("[vocab_id] = @p3", baked, StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    // In(params T[]) given an array has no element count in the syntax.
    [Fact]
    public void In_over_an_array_variable_does_not_bake()
    {
        var generated = Generated("""
            using System.Threading.Tasks;
            using Mizzle.SqlServer;

            namespace Demo;

            public record InRow(string VocabId);

            public static class InArrayQ
            {
                public static async Task Run(SqlDb db, decimal[] types)
                {
                    var v = new Vocab();
                    var rows = await db.Select(v.VocabId)
                        .From(v)
                        .Where(v.TypeId.In(types))
                        .ToListAsync<InRow>();
                }
            }
            """);

        Assert.DoesNotContain("var sql = ", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Case_bakes_the_same_sql_the_runtime_emits()
    {
        var v = new Vocab();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(
                v.VocabId,
                Sql.As(Sql.Case(
                        Sql.When(v.TypeId.Eq(504m), 0),
                        Sql.When(v.TypeId.Eq(502m), 1))
                    .Else(Sql.Value(4)), "pri"))
            .From(v.ToFrom()));

        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            public record CaseRow(string VocabId, int Pri);

            public static class CaseQ
            {
                public static async Task Run(SqlDb db)
                {
                    var v = new Vocab();
                    var rows = await db.Select(
                            v.VocabId,
                            Sql.As(Sql.Case(
                                    Sql.When(v.TypeId.Eq(504m), 0),
                                    Sql.When(v.TypeId.Eq(502m), 1))
                                .Else(Sql.Value(4)), "pri"))
                        .From(v)
                        .ToListAsync<CaseRow>();
                }
            }
            """);

        Assert.Contains(
            "CASE WHEN [revdel0].[type_id] = @p0 THEN @p1 WHEN [revdel0].[type_id] = @p2 THEN @p3 ELSE @p4 END AS [pri]",
            baked,
            StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    [Fact]
    public void Case_without_an_else_bakes_the_same_sql_the_runtime_emits()
    {
        var v = new Vocab();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(Sql.As(Sql.Case(Sql.When(v.TypeId.Eq(504m), 0)), "pri"))
            .From(v.ToFrom()));

        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            public record CaseRow(int? Pri);

            public static class NoElseQ
            {
                public static async Task Run(SqlDb db)
                {
                    var v = new Vocab();
                    var rows = await db.Select(
                            Sql.As(Sql.Case(Sql.When(v.TypeId.Eq(504m), 0)), "pri"))
                        .From(v)
                        .ToListAsync<CaseRow>();
                }
            }
            """);

        Assert.Contains("CASE WHEN [revdel0].[type_id] = @p0 THEN @p1 END AS [pri]", baked, StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    [Fact]
    public void Case_over_columns_and_functions_bakes_without_binds()
    {
        var v = new Vocab();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(Sql.As(Sql.Case(Sql.When(v.Code.Eq(v.VocabId), TSql.RTrim(v.Code))), "pick"))
            .From(v.ToFrom()));

        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            public record PickRow(string? Pick);

            public static class PickQ
            {
                public static async Task Run(SqlDb db)
                {
                    var v = new Vocab();
                    var rows = await db.Select(
                            Sql.As(Sql.Case(Sql.When(v.Code.Eq(v.VocabId), TSql.RTrim(v.Code))), "pick"))
                        .From(v)
                        .ToListAsync<PickRow>();
                }
            }
            """);

        Assert.Contains(
            "CASE WHEN [revdel0].[code] = [revdel0].[vocab_id] THEN rtrim([revdel0].[code]) END AS [pick]",
            baked,
            StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    // The point of the pair: IN plus CASE replaces the five-branch UNION ALL ladder.
    [Fact]
    public void The_priority_ladder_bakes_as_one_select()
    {
        var v = new Vocab();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(
                v.VocabId,
                v.Code,
                Sql.As(Sql.Case(
                        Sql.When(v.TypeId.Eq(504m), 0),
                        Sql.When(v.TypeId.Eq(502m), 1),
                        Sql.When(v.TypeId.Eq(503m), 2),
                        Sql.When(v.TypeId.Eq(501m), 3))
                    .Else(Sql.Value(4)), "pri"))
            .From(v.ToFrom())
            .Where(v.TypeId.In(504m, 502m, 503m, 501m, 505m)));

        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            public record LadderRow(string VocabId, string? Code, int Pri);

            public static class LadderQ
            {
                public static async Task Run(SqlDb db)
                {
                    var v = new Vocab();
                    var rows = await db.Select(
                            v.VocabId,
                            v.Code,
                            Sql.As(Sql.Case(
                                    Sql.When(v.TypeId.Eq(504m), 0),
                                    Sql.When(v.TypeId.Eq(502m), 1),
                                    Sql.When(v.TypeId.Eq(503m), 2),
                                    Sql.When(v.TypeId.Eq(501m), 3))
                                .Else(Sql.Value(4)), "pri"))
                        .From(v)
                        .Where(v.TypeId.In(504m, 502m, 503m, 501m, 505m))
                        .ToListAsync<LadderRow>();
                }
            }
            """);

        // Select-item binds come first, then the WHERE list: @p0..@p8 for the
        // CASE, @p9..@p13 for the IN.
        Assert.Contains("ELSE @p8 END AS [pri]", baked, StringComparison.Ordinal);
        Assert.Contains("[type_id] IN (@p9, @p10, @p11, @p12, @p13)", baked, StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }
}
