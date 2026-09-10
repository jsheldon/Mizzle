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

public sealed class RowNumberBakeTests
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
    public void RowNumber_bakes_the_same_sql_the_runtime_emits()
    {
        var v = new Vocab();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(
                v.VocabId,
                Sql.As(Sql.RowNumber().PartitionBy(v.VocabId).OrderBy(v.Code).OrderByDesc(v.TypeId), "rn"))
            .From(v.ToFrom()));

        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            public record RankedRow(string VocabId, int Rn);

            public static class RankedQ
            {
                public static async Task Run(SqlDb db)
                {
                    var v = new Vocab();
                    var rows = await db.Select(
                            v.VocabId,
                            Sql.As(Sql.RowNumber().PartitionBy(v.VocabId).OrderBy(v.Code).OrderByDesc(v.TypeId), "rn"))
                        .From(v)
                        .ToListAsync<RankedRow>();
                }
            }
            """);

        Assert.Contains(
            "ROW_NUMBER() OVER (PARTITION BY [revdel0].[vocab_id] "
            + "ORDER BY [revdel0].[code], [revdel0].[type_id] DESC) AS [rn]",
            baked,
            StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    [Fact]
    public void RowNumber_without_partition_bakes_the_same_sql_the_runtime_emits()
    {
        var v = new Vocab();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(Sql.As(Sql.RowNumber().OrderBy(v.Code), "rn"))
            .From(v.ToFrom()));

        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            public record RankedRow(int? Rn);

            public static class NoPartitionQ
            {
                public static async Task Run(SqlDb db)
                {
                    var v = new Vocab();
                    var rows = await db.Select(Sql.As(Sql.RowNumber().OrderBy(v.Code), "rn"))
                        .From(v)
                        .ToListAsync<RankedRow>();
                }
            }
            """);

        Assert.Contains("ROW_NUMBER() OVER (ORDER BY [revdel0].[code]) AS [rn]", baked, StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    [Fact]
    public void RowNumber_over_an_unresolvable_expression_does_not_bake()
    {
        // Sql.Coalesce is a valid runtime expression but is not one of the
        // scalar positions ResolveScalarSql understands, so the select item
        // -- and the whole chain -- falls back to the runtime path.
        var generated = Generated("""
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            public record RankedRow(int? Rn);

            public static class UnresolvableQ
            {
                public static async Task Run(SqlDb db)
                {
                    var v = new Vocab();
                    var rows = await db.Select(
                            Sql.As(Sql.RowNumber().OrderBy(Sql.Coalesce(v.Code, v.VocabId)), "rn"))
                        .From(v)
                        .ToListAsync<RankedRow>();
                }
            }
            """);

        Assert.DoesNotContain("var sql = ", generated, StringComparison.Ordinal);
    }
}
