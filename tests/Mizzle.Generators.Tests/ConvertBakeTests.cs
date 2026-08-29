using Microsoft.CodeAnalysis.CSharp;
using Mizzle.Compile;
using Mizzle.Fluent;
using Mizzle.SqlServer;

namespace Mizzle.Generators.Tests;

file sealed class Products : SqlTable<Products>
{
    public Products() : base("products", "dbo") { }
    public SqlColumn<string> VocabId { get; } = VarChar("vocab_id", 20).NotNull();
    public SqlColumn<int> MedId { get; } = Int("medid");
}

public sealed class ConvertBakeTests
{
    private const string Tables = """
        using Mizzle.SqlServer;

        namespace Demo;

        public sealed class Products : SqlTable<Products>
        {
            public Products() : base("products", "dbo") { }
            public SqlColumn<string> VocabId { get; } = VarChar("vocab_id", 20).NotNull();
            public SqlColumn<int> MedId { get; } = Int("medid");
        }
        """;

    private static string RuntimeSql(SelectBuilder builder)
    {
        var (canonical, values) = Parameterizer.Run(builder.Build());
        return new SqlServerEmitter().Emit(canonical, values).Sql;
    }

    private static string BakedSql(string callSite)
    {
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(Tables, callSite));
        const string open = "            var sql = ";
        var start = generated.IndexOf(open, StringComparison.Ordinal);
        Assert.True(start >= 0, "no baked SQL assignment found in generated output:\n" + generated);
        start += open.Length;
        var lineEnd = generated.IndexOf('\n', start);
        Assert.True(lineEnd > start, "baked SQL assignment was not terminated");
        return generated.Substring(start, lineEnd - start).TrimEnd('\r').TrimEnd(';');
    }

    [Fact]
    public void Nested_convert_on_eq_rhs_bakes_the_same_sql_the_runtime_emits()
    {
        var p = new Products();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(p.VocabId)
            .From(p.ToFrom())
            .Where(p.VocabId.Eq(TSql.Convert(SqlType.VarChar(20), TSql.Convert(SqlType.Int, p.MedId)))));

        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.SqlServer;

            namespace Demo;

            public record ConvertRow(string VocabId);

            public static class ConvertQ
            {
                public static async Task Run(SqlDb db)
                {
                    var p = new Products();
                    var rows = await db.Select(p.VocabId)
                        .From(p)
                        .Where(p.VocabId.Eq(
                            TSql.Convert(SqlType.VarChar(20), TSql.Convert(SqlType.Int, p.MedId))))
                        .ToListAsync<ConvertRow>();
                }
            }
            """);

        Assert.Contains("CONVERT(varchar(20), CONVERT(int, [products].[medid]))", baked, StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    [Fact]
    public void Convert_in_select_list_bakes_with_alias()
    {
        var p = new Products();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(Sql.As(TSql.Convert(SqlType.VarChar(20), TSql.Convert(SqlType.Int, p.MedId)), "Rxnorm"))
            .From(p.ToFrom()));

        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            public record RxRow(string Rxnorm);

            public static class RxQ
            {
                public static async Task Run(SqlDb db)
                {
                    var p = new Products();
                    var rows = await db.Select(Sql.As(
                            TSql.Convert(SqlType.VarChar(20), TSql.Convert(SqlType.Int, p.MedId)),
                            "Rxnorm"))
                        .From(p)
                        .ToListAsync<RxRow>();
                }
            }
            """);

        Assert.Contains("CONVERT(varchar(20), CONVERT(int, [products].[medid])) AS [Rxnorm]", baked, StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    // The legacy "still active" predicate compares a char(8) date column to
    // CONVERT(char(8), GETDATE(), 112); without the style code that is
    // unwritable, and an unwritable predicate means a raw-SQL escape hatch.
    [Fact]
    public void Convert_with_a_style_code_bakes_the_same_sql_the_runtime_emits()
    {
        var p = new Products();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(p.VocabId)
            .From(p.ToFrom())
            .Where(p.VocabId.Gt(TSql.Convert(SqlType.Char(8), TSql.GetDate(), 112))));

        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.SqlServer;

            namespace Demo;

            public record StyleRow(string VocabId);

            public static class StyleQ
            {
                public static async Task Run(SqlDb db)
                {
                    var p = new Products();
                    var rows = await db.Select(p.VocabId)
                        .From(p)
                        .Where(p.VocabId.Gt(TSql.Convert(SqlType.Char(8), TSql.GetDate(), 112)))
                        .ToListAsync<StyleRow>();
                }
            }
            """);

        Assert.Contains("CONVERT(char(8), getdate(), 112)", baked, StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    [Fact]
    public void Convert_to_varchar_max_bakes_the_same_sql_the_runtime_emits()
    {
        var p = new Products();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(Sql.As(TSql.Convert(SqlType.VarCharMax, p.MedId), "Wide"))
            .From(p.ToFrom()));

        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            public record WideRow(string Wide);

            public static class WideQ
            {
                public static async Task Run(SqlDb db)
                {
                    var p = new Products();
                    var rows = await db.Select(
                            Sql.As(TSql.Convert(SqlType.VarCharMax, p.MedId), "Wide"))
                        .From(p)
                        .ToListAsync<WideRow>();
                }
            }
            """);

        Assert.Contains("CONVERT(varchar(max), [products].[medid]) AS [Wide]", baked, StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    // A style code the baker cannot read as a literal must drop the query to the
    // runtime path rather than bake CONVERT without it.
    [Fact]
    public void Convert_with_a_non_literal_style_does_not_bake()
    {
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(Tables, """
            using System.Threading.Tasks;
            using Mizzle.SqlServer;

            namespace Demo;

            public record StyleRow(string VocabId);

            public static class DynamicStyleQ
            {
                public static async Task Run(SqlDb db, int style)
                {
                    var p = new Products();
                    var rows = await db.Select(p.VocabId)
                        .From(p)
                        .Where(p.VocabId.Gt(TSql.Convert(SqlType.Char(8), TSql.GetDate(), style)))
                        .ToListAsync<StyleRow>();
                }
            }
            """));

        // Not merely "no CONVERT": the query must not bake at all. Baking the
        // comparison with its right side dropped to a bind would run different
        // SQL than the runtime emitter.
        Assert.DoesNotContain("var sql = ", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Rtrim_and_getdate_bake_the_same_sql_the_runtime_emits()
    {
        var p = new Products();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(Sql.As(TSql.RTrim(p.VocabId), "Trimmed"))
            .From(p.ToFrom())
            .Where(p.VocabId.Lt(TSql.Convert(SqlType.Char(8), TSql.GetDate(), 112))));

        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.SqlServer;

            namespace Demo;

            public record TrimRow(string Trimmed);

            public static class TrimQ
            {
                public static async Task Run(SqlDb db)
                {
                    var p = new Products();
                    var rows = await db.Select(Sql.As(TSql.RTrim(p.VocabId), "Trimmed"))
                        .From(p)
                        .Where(p.VocabId.Lt(TSql.Convert(SqlType.Char(8), TSql.GetDate(), 112)))
                        .ToListAsync<TrimRow>();
                }
            }
            """);

        Assert.Contains("rtrim([products].[vocab_id]) AS [Trimmed]", baked, StringComparison.Ordinal);
        Assert.Contains("< CONVERT(char(8), getdate(), 112)", baked, StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    // A TSql function the baker does not know must drop the query to the runtime
    // path, not bake its right side as a parameter that is never supplied.
    [Fact]
    public void An_unrenderable_expr_right_side_does_not_bake()
    {
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(Tables, """
            using System.Threading.Tasks;
            using Mizzle.SqlServer;

            namespace Demo;

            public record R(string VocabId);

            public static class UnknownFnQ
            {
                public static async Task Run(SqlDb db)
                {
                    var p = new Products();
                    var rows = await db.Select(p.VocabId)
                        .From(p)
                        .Where(p.VocabId.Gt(TSql.Convert(SqlType.Char(8), TSql.Len(p.VocabId))))
                        .ToListAsync<R>();
                }
            }
            """));

        Assert.DoesNotContain("var sql = ", generated, StringComparison.Ordinal);
    }

    // A plain value right side still binds.
    [Fact]
    public void A_value_right_side_still_bakes_as_a_bind()
    {
        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.SqlServer;

            namespace Demo;

            public record R(string VocabId);

            public static class ValueQ
            {
                public static async Task Run(SqlDb db, string wanted)
                {
                    var p = new Products();
                    var rows = await db.Select(p.VocabId)
                        .From(p)
                        .Where(p.VocabId.Eq(wanted))
                        .Where(p.VocabId.Like("a%"))
                        .ToListAsync<R>();
                }
            }
            """);

        Assert.Contains("[vocab_id] = @p0", baked, StringComparison.Ordinal);
        Assert.Contains("[vocab_id] LIKE @p1", baked, StringComparison.Ordinal);
    }

    // A join key used in two places has to be written twice unless a local can
    // hold it -- and two copies that must stay identical is how they stop being
    // identical.
    [Fact]
    public void A_convert_held_in_a_local_bakes_the_same_sql_as_the_inline_form()
    {
        var p = new Products();
        var runtime = RuntimeSql(new SelectBuilder()
            .Select(p.VocabId)
            .From(p.ToFrom())
            .Where(p.VocabId.Eq(TSql.Convert(SqlType.VarChar(20), TSql.Convert(SqlType.Int, p.MedId)))));

        var baked = BakedSql("""
            using System.Threading.Tasks;
            using Mizzle.SqlServer;

            namespace Demo;

            public record HoistRow(string VocabId);

            public static class HoistQ
            {
                public static async Task Run(SqlDb db)
                {
                    var p = new Products();
                    var medIdKey = TSql.Convert(SqlType.VarChar(20), TSql.Convert(SqlType.Int, p.MedId));
                    var rows = await db.Select(p.VocabId)
                        .From(p)
                        .Where(p.VocabId.Eq(medIdKey))
                        .ToListAsync<HoistRow>();
                }
            }
            """);

        Assert.Contains("CONVERT(varchar(20), CONVERT(int, [products].[medid]))", baked, StringComparison.Ordinal);
        Assert.Equal(SymbolDisplay.FormatLiteral(runtime, quote: true), baked);
    }

    // Reading the initializer is only sound while the initializer is still what
    // the local holds.
    [Fact]
    public void A_reassigned_expr_local_does_not_bake()
    {
        var generated = GeneratorTestHost.Generated(GeneratorTestHost.Run(Tables, """
            using System.Threading.Tasks;
            using Mizzle.Ir;
            using Mizzle.SqlServer;

            namespace Demo;

            public record R(string VocabId);

            public static class ReassignQ
            {
                public static async Task Run(SqlDb db, bool wide)
                {
                    var p = new Products();
                    Expr medIdKey = TSql.Convert(SqlType.VarChar(20), TSql.Convert(SqlType.Int, p.MedId));
                    if (wide)
                    {
                        medIdKey = TSql.Convert(SqlType.VarChar(50), p.MedId);
                    }

                    var rows = await db.Select(p.VocabId)
                        .From(p)
                        .Where(p.VocabId.Eq(medIdKey))
                        .ToListAsync<R>();
                }
            }
            """));

        Assert.DoesNotContain("var sql = ", generated, StringComparison.Ordinal);
    }
}
