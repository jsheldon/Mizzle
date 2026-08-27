namespace Mizzle.Tests;

public sealed class CteEmitterTests
{
    [Fact]
    public void Recursive_cte_union_all()
    {
        var sql = new PgEmitter().Emit(RecursiveOuter(), []);
        Assert.StartsWith("WITH RECURSIVE \"t\" AS (", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("UNION ALL", sql.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlServer_recursive_cte_emits_with()
    {
        var sql = new SqlServerEmitter().Emit(RecursiveOuter(), []);
        Assert.StartsWith("WITH [t] AS (", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("UNION ALL", sql.Sql, StringComparison.Ordinal);
    }

    private static SelectQuery RecursiveOuter()
    {
        var seed = new SelectQuery(
            Select: [new SelectItem(new ColumnRef("c", "id", typeof(int)), null)],
            From: new FromSource("categories", "public", "c"),
            Joins: [],
            Where: null,
            OrderBy: [],
            Limit: null,
            Offset: null,
            Distinct: false,
            With: [],
            RecursiveWith: false,
            UnionAll: []);
        var cteBody = seed with { UnionAll = [seed] };
        return seed with
        {
            From = new FromSource("t", null, "t"),
            Select = [new SelectItem(new ColumnRef("t", "id", typeof(int)), null)],
            With = [new CteClause("t", cteBody)],
            RecursiveWith = true
        };
    }
}
