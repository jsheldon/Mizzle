using Microsoft.CodeAnalysis;

namespace Mizzle.Generators.Tests;

public sealed class AlwaysFilterAnalyzerTests
{
    private static string Program(string chain, string extraTables = "", string extras = "") => $$"""
        using System.Threading.Tasks;
        using Mizzle.Fluent;
        using Mizzle.SqlServer;

        namespace Demo;

        public sealed class Orders : SqlTable<Orders>
        {
            public Orders() : base("orders", "dbo") { }
            public SqlColumn<int> Id { get; } = Int("id").PrimaryKey();
            public SqlColumn<int> TenantId { get; } = Int("tenant_id").NotNull().AlwaysFilter();
            public SqlColumn<int> Qty { get; } = Int("qty").NotNull();
        }

        {{extraTables}}

        public static class Q
        {
            public static async Task Run(SqlDb db, int tenant, int qty)
            {
                var o = new Orders();
                {{extras}}
                _ = await {{chain}};
            }
        }
        """;

    [Fact]
    public void Select_without_the_column_in_where_reports_MIZ013()
    {
        var diagnostics = GeneratorTestHost.Analyze(
            Program("db.Select(o.Id).From(o).ToListAsync(static r => r.GetInt32(0))"),
            null);
        var diagnostic = Assert.Single(diagnostics, d => d.Id == "MIZ013");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("TenantId", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Select_with_the_column_in_where_reports_nothing()
    {
        var diagnostics = GeneratorTestHost.Analyze(
            Program("db.Select(o.Id).From(o).Where(o.TenantId.Eq(tenant)).ToListAsync(static r => r.GetInt32(0))"),
            null);
        Assert.DoesNotContain(diagnostics, d => d.Id == "MIZ013");
    }

    [Fact]
    public void WhereIf_does_not_satisfy_AlwaysFilter()
    {
        var diagnostics = GeneratorTestHost.Analyze(
            Program("db.Select(o.Id).From(o).WhereIf(qty > 0, o.TenantId.Eq(tenant)).ToListAsync(static r => r.GetInt32(0))"),
            null);
        Assert.Contains(diagnostics, d => d.Id == "MIZ013");
    }

    [Fact]
    public void Join_on_does_not_satisfy_AlwaysFilter()
    {
        const string customers = """
            public sealed class Customers : SqlTable<Customers>
            {
                public Customers() : base("customers", "dbo") { }
                public SqlColumn<int> Id { get; } = Int("id").PrimaryKey();
            }
            """;
        var diagnostics = GeneratorTestHost.Analyze(
            Program(
                "db.Select(o.Id).From(o).InnerJoin(c).On(o.TenantId.Eq(c.Id)).ToListAsync(static r => r.GetInt32(0))",
                customers,
                "var c = new Customers();"),
            null);
        Assert.Contains(diagnostics, d => d.Id == "MIZ013");
    }

    [Fact]
    public void Update_without_the_column_in_where_reports_MIZ013()
    {
        var diagnostics = GeneratorTestHost.Analyze(
            Program("db.Update(o).Set(o.Qty, qty).ExecuteAsync()"),
            null);
        Assert.Contains(diagnostics, d => d.Id == "MIZ013");
    }

    [Fact]
    public void Update_with_the_column_in_where_reports_nothing()
    {
        var diagnostics = GeneratorTestHost.Analyze(
            Program("db.Update(o).Set(o.Qty, qty).Where(o.TenantId.Eq(tenant)).ExecuteAsync()"),
            null);
        Assert.DoesNotContain(diagnostics, d => d.Id == "MIZ013");
    }

    [Fact]
    public void Delete_without_the_column_in_where_reports_MIZ013()
    {
        var diagnostics = GeneratorTestHost.Analyze(
            Program("db.DeleteFrom(o).ExecuteAsync()"),
            null);
        Assert.Contains(diagnostics, d => d.Id == "MIZ013");
    }

    [Fact]
    public void Delete_with_the_column_in_where_reports_nothing()
    {
        var diagnostics = GeneratorTestHost.Analyze(
            Program("db.DeleteFrom(o).Where(o.TenantId, tenant).ExecuteAsync()"),
            null);
        Assert.DoesNotContain(diagnostics, d => d.Id == "MIZ013");
    }

    // A CTE body is its own scope -- the outer WHERE does not constrain it --
    // so it is exactly where a tenant filter is easiest to lose.
    [Fact]
    public void Cte_body_without_the_column_in_where_reports_MIZ013()
    {
        var diagnostics = GeneratorTestHost.Analyze(
            Program(
                """
                db.Select(o.Id)
                    .With(CteBuilder.Named("all_orders", db.Select(o.Id).From(o).Build()))
                    .From(o)
                    .Where(o.TenantId.Eq(tenant))
                    .ToListAsync(static r => r.GetInt32(0))
                """),
            null);
        Assert.Contains(diagnostics, d => d.Id == "MIZ013");
    }

    [Fact]
    public void Cte_body_with_the_column_in_where_reports_nothing()
    {
        var diagnostics = GeneratorTestHost.Analyze(
            Program(
                """
                db.Select(o.Id)
                    .With(CteBuilder.Named(
                        "all_orders",
                        db.Select(o.Id).From(o).Where(o.TenantId.Eq(tenant)).Build()))
                    .From(o)
                    .Where(o.TenantId.Eq(tenant))
                    .ToListAsync(static r => r.GetInt32(0))
                """),
            null);
        Assert.DoesNotContain(diagnostics, d => d.Id == "MIZ013");
    }

    [Fact]
    public void Cte_body_joining_an_unfiltered_table_reports_MIZ013()
    {
        var diagnostics = GeneratorTestHost.Analyze(
            Program(
                """
                db.Select(o.Id)
                    .With(CteBuilder.Named(
                        "joined",
                        db.Select(o.Id).From(o2).InnerJoin(o).On(o.Id.Eq(o2.Id)).Build()))
                    .From(o)
                    .Where(o.TenantId.Eq(tenant))
                    .ToListAsync(static r => r.GetInt32(0))
                """,
                extras: "var o2 = new Orders().WithAlias(\"o2\");"),
            null);
        Assert.Contains(diagnostics, d => d.Id == "MIZ013");
    }

    [Fact]
    public void Cte_body_on_an_update_without_the_column_reports_MIZ013()
    {
        var diagnostics = GeneratorTestHost.Analyze(
            Program(
                """
                db.Update(o)
                    .With(CteBuilder.Named("all_orders", db.Select(o.Id).From(o).Build()))
                    .Set(o.Qty, qty)
                    .Where(o.TenantId.Eq(tenant))
                    .ExecuteAsync()
                """),
            null);
        Assert.Contains(diagnostics, d => d.Id == "MIZ013");
    }

    [Fact]
    public void Cte_body_on_a_delete_without_the_column_reports_MIZ013()
    {
        var diagnostics = GeneratorTestHost.Analyze(
            Program(
                """
                db.DeleteFrom(o)
                    .With(CteBuilder.Named("all_orders", db.Select(o.Id).From(o).Build()))
                    .Where(o.TenantId.Eq(tenant))
                    .ExecuteAsync()
                """),
            null);
        Assert.Contains(diagnostics, d => d.Id == "MIZ013");
    }

    // An unresolvable CTE body is skipped, not treated as a reason to give up on
    // the statement it hangs off.
    [Fact]
    public void Unresolvable_cte_body_still_leaves_the_outer_statement_checked()
    {
        var diagnostics = GeneratorTestHost.Analyze(
            Program(
                """
                db.Update(o)
                    .With(CteBuilder.Named(name, db.Select(o.Id).From(o).Build()))
                    .Set(o.Qty, qty)
                    .ExecuteAsync()
                """,
                extras: "var name = \"all_orders\";"),
            null);
        Assert.Contains(diagnostics, d => d.Id == "MIZ013");
    }

    [Fact]
    public void Union_branch_without_the_column_in_where_reports_MIZ013()
    {
        var diagnostics = GeneratorTestHost.Analyze(
            Program(
                """
                db.Select(o.Id)
                    .From(o)
                    .Where(o.TenantId.Eq(tenant))
                    .UnionAll(db.Select(o.Id).From(o))
                    .ToListAsync(static r => r.GetInt32(0))
                """),
            null);
        Assert.Contains(diagnostics, d => d.Id == "MIZ013");
    }

    // Every diagnostic lands on the terminator, so one column missed in several
    // scopes must not stack identical warnings on a single line.
    [Fact]
    public void One_column_missed_in_many_scopes_reports_once()
    {
        var diagnostics = GeneratorTestHost.Analyze(
            Program(
                """
                db.Select(o.Id)
                    .With(CteBuilder.Named("a", db.Select(o.Id).From(o).Build()))
                    .With(CteBuilder.Named("b", db.Select(o.Id).From(o).Build()))
                    .From(o)
                    .UnionAll(db.Select(o.Id).From(o))
                    .UnionAll(db.Select(o.Id).From(o))
                    .ToListAsync(static r => r.GetInt32(0))
                """),
            null);
        Assert.Single(diagnostics, d => d.Id == "MIZ013");
    }

    [Fact]
    public void Table_without_AlwaysFilter_reports_nothing()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mizzle.SqlServer;

            namespace Demo;

            public sealed class Widgets : SqlTable<Widgets>
            {
                public Widgets() : base("widgets", "dbo") { }
                public SqlColumn<int> Id { get; } = Int("id").PrimaryKey();
            }

            public static class Q
            {
                public static async Task Run(SqlDb db)
                {
                    var w = new Widgets();
                    _ = await db.Select(w.Id).From(w).ToListAsync(static r => r.GetInt32(0));
                }
            }
            """;
        var diagnostics = GeneratorTestHost.Analyze(source, null);
        Assert.DoesNotContain(diagnostics, d => d.Id == "MIZ013");
    }
}
