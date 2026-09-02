using System.Text;

namespace Mizzle.Generators;

internal sealed class BakedColumn
{
    public BakedColumn(string tableAlias, string dbName, string propertyName, string clrTypeName, bool isRequired, string readerCall, string? readConverter = null, string? projectionName = null, bool isUntrimmed = false, string? sqlExpression = null, bool isLiteral = false)
    {
        TableAlias = tableAlias;
        DbName = dbName;
        PropertyName = propertyName;
        ClrTypeName = clrTypeName;
        IsRequired = isRequired;
        ReaderCall = readerCall;
        ReadConverter = readConverter;
        ProjectionName = projectionName;
        IsUntrimmed = isUntrimmed;
        SqlExpression = sqlExpression;
        IsLiteral = isLiteral;
    }

    public string TableAlias { get; }
    public string DbName { get; }
    public string PropertyName { get; }
    public string ClrTypeName { get; }
    public bool IsRequired { get; }
    public string ReaderCall { get; }
    public string? ReadConverter { get; }

    // Set by .As("...") at the select site.
    public string? ProjectionName { get; }

    // The name member matching and generated records use.
    public string MemberName => ProjectionName ?? PropertyName;

    // Untrimmed() on the column declaration.
    public bool IsUntrimmed { get; }

    // Set for a projected expression (an aggregate) rather than a table column.
    // The SQL is already rendered; the CLR type comes from the target member,
    // because an aggregate's result type is dialect-specific.
    public string? SqlExpression { get; }

    // A literal projected into the select list. Emits a placeholder and consumes a
    // bind slot; the value is supplied at run time by the same builder.
    public bool IsLiteral { get; }

    public bool IsExpression => SqlExpression is not null || IsLiteral;
}

// A table as used by one query: its schema facts plus the alias this particular
// instance carries. Two instances of the same table differ only by alias.
internal sealed class BakedTable
{
    public BakedTable(TableFactsModel facts, string alias)
    {
        Facts = facts;
        Alias = alias;
    }

    public TableFactsModel Facts { get; }
    public string Alias { get; }
}

// col.Eq(col) when RightAlias/RightDbName are set; otherwise col.Eq(<runtime bind>).
internal sealed class BakedCondition
{
    public BakedCondition(string leftAlias, string leftDbName, string? rightAlias, string? rightDbName, string? leftExpression = null, int? conditionalIndex = null, string op = "=", bool isUnary = false, string? rightExpression = null)
    {
        LeftExpression = leftExpression;
        ConditionalIndex = conditionalIndex;
        Op = op;
        IsUnary = isUnary;
        LeftAlias = leftAlias;
        LeftDbName = leftDbName;
        RightAlias = rightAlias;
        RightDbName = rightDbName;
        RightExpression = rightExpression;
    }

    // A Sql.And(...)/Sql.Or(...) group: Children render parenthesized, joined
    // by Combinator, in argument order. That order is what has to match the
    // runtime Parameterizer's left-to-right walk of the same Expr tree -- SQL
    // AND/OR being associative, a flat join is equivalent to the tree Sql.Or's
    // params-array overload folds at runtime, so no nesting is needed here.
    public BakedCondition(string combinator, IReadOnlyList<BakedCondition> children, int? conditionalIndex = null)
    {
        Combinator = combinator;
        Children = children;
        ConditionalIndex = conditionalIndex;
        LeftAlias = "";
        LeftDbName = "";
        Op = "";
    }

    // Set when the left side is an aggregate rather than a column, as in HAVING.
    public string? LeftExpression { get; }

    // Set when the right side is a baked expression (TSql.Convert) rather than
    // a column or a bind placeholder.
    public string? RightExpression { get; }

    // Set for a WhereIf predicate: the bit in the shape mask that decides whether
    // this condition is part of a given variant. Null means always applied.
    public int? ConditionalIndex { get; }

    // The SQL comparison operator, or the trailing form for a unary test.
    public string Op { get; }

    // IS NULL / IS NOT NULL: no right-hand side at all.
    public bool IsUnary { get; }

    public string LeftAlias { get; }
    public string LeftDbName { get; }
    public string? RightAlias { get; }
    public string? RightDbName { get; }

    // "AND" or "OR" for a composite condition; null for a leaf comparison.
    public string? Combinator { get; }
    public IReadOnlyList<BakedCondition>? Children { get; }

    public bool IsBind => RightAlias is null && RightExpression is null;

    // Carries this condition (leaf or composite) into a WhereIf variant slot
    // without losing composite structure -- a plain field-copy constructor
    // call would silently drop Combinator/Children.
    public BakedCondition WithConditionalIndex(int index)
        => Combinator is { } combinator
            ? new BakedCondition(combinator, Children!, index)
            : new BakedCondition(LeftAlias, LeftDbName, RightAlias, RightDbName, LeftExpression, index, Op, IsUnary, RightExpression);
}

internal sealed class BakedJoin
{
    public BakedJoin(bool isLeft, BakedTable table, IReadOnlyList<BakedCondition> on)
    {
        IsLeft = isLeft;
        Table = table;
        On = on;
    }

    public bool IsLeft { get; }
    public BakedTable Table { get; }
    public IReadOnlyList<BakedCondition> On { get; }
}

// A CTE as written at the query site: its name plus the baked body select.
internal sealed class BakedCte
{
    public BakedCte(string name, BakedQuerySpec body)
    {
        Name = name;
        Body = body;
    }

    public string Name { get; }
    public BakedQuerySpec Body { get; }
}

internal sealed class BakedQuerySpec
{
    public BakedQuerySpec(
        bool isPostgres,
        BakedTable from,
        IReadOnlyList<BakedJoin> joins,
        IReadOnlyList<BakedColumn> select,
        bool distinct,
        IReadOnlyList<BakedCondition> where,
        IReadOnlyList<(string Alias, string DbName, bool Desc)> orderBy,
        int? limit,
        int? offset,
        IReadOnlyList<BakedCte> with,
        bool recursiveWith,
        IReadOnlyList<(string Alias, string DbName)> groupBy,
        IReadOnlyList<BakedCondition> having,
        IReadOnlyList<BakedQuerySpec> unionAll,
        int conditionalCount)
    {
        IsPostgres = isPostgres;
        From = from;
        Joins = joins;
        Select = select;
        Distinct = distinct;
        Where = where;
        OrderBy = orderBy;
        Limit = limit;
        Offset = offset;
        With = with;
        RecursiveWith = recursiveWith;
        GroupBy = groupBy;
        Having = having;
        UnionAll = unionAll;
        ConditionalCount = conditionalCount;
    }

    public bool IsPostgres { get; }
    public BakedTable From { get; }
    public IReadOnlyList<BakedJoin> Joins { get; }
    public IReadOnlyList<BakedColumn> Select { get; }
    public bool Distinct { get; }
    public IReadOnlyList<BakedCondition> Where { get; }
    public IReadOnlyList<(string Alias, string DbName, bool Desc)> OrderBy { get; }
    public int? Limit { get; }
    public int? Offset { get; }
    public IReadOnlyList<BakedCte> With { get; }
    public bool RecursiveWith { get; }
    public IReadOnlyList<(string Alias, string DbName)> GroupBy { get; }
    public IReadOnlyList<BakedCondition> Having { get; }

    // How many WhereIf predicates the chain has; the generator bakes one variant
    // per combination.
    public int ConditionalCount { get; }
    public IReadOnlyList<BakedQuerySpec> UnionAll { get; }
}

// Mirrors PgEmitter/SqlServerEmitter output for the statically-visible subset,
// with bind slots numbered in Parameterizer order: join conditions (join order,
// condition order), then where conditions. Returns null when the shape cannot
// be baked (SQL Server paging without ORDER BY).
internal static class BakedSqlEmitter
{
    // The number of conditionals past which the combinations stop being worth
    // baking; such a chain falls back to the runtime path.
    public const int MaxBakedConditionals = 4;

    public static string? Emit(BakedQuerySpec spec) => Emit(spec, ulong.MaxValue);

    // mask: one bit per WhereIf, in chain order. A clear bit omits that predicate.
    public static string? Emit(BakedQuerySpec spec, ulong mask)
    {
        var slot = 0;
        return Emit(spec, ref slot, includeWith: true, mask);
    }

    // Parameterizer order for a select is: With CTEs -> select items -> joins ->
    // where, so a CTE body's binds must take the lowest slots.
    private static string? Emit(BakedQuerySpec spec, ref int slot, bool includeWith, ulong mask = ulong.MaxValue)
    {
        if (!spec.IsPostgres && (spec.Limit is not null || spec.Offset is not null) && spec.OrderBy.Count == 0)
        {
            return null;
        }

        var sql = new StringBuilder();
        if (includeWith && spec.With.Count > 0)
        {
            sql.Append(spec.RecursiveWith ? "WITH RECURSIVE " : "WITH ");
            for (var i = 0; i < spec.With.Count; i++)
            {
                if (i > 0)
                {
                    sql.Append(", ");
                }

                sql.Append(Quote(spec, spec.With[i].Name));
                sql.Append(" AS (");
                var body = Emit(spec.With[i].Body, ref slot, includeWith: false, mask);
                if (body is null)
                {
                    return null;
                }

                sql.Append(body);
                sql.Append(')');
            }

            sql.Append(' ');
        }

        sql.Append("SELECT ");
        if (spec.Distinct)
        {
            sql.Append("DISTINCT ");
        }

        var selectItems = new List<string>();
        foreach (var item in spec.Select)
        {
            selectItems.Add(SelectItem(spec, item, ref slot));
        }

        sql.Append(string.Join(", ", selectItems));
        sql.Append(" FROM ");
        sql.Append(Table(spec, spec.From));
        foreach (var join in spec.Joins)
        {
            sql.Append(join.IsLeft ? " LEFT JOIN " : " INNER JOIN ");
            sql.Append(Table(spec, join.Table));
            sql.Append(" ON ");
            sql.Append(FoldConditions(spec, join.On, ref slot));
        }

        var where = spec.Where
            .Where(c => c.ConditionalIndex is not { } bit || (mask & (1UL << bit)) != 0)
            .ToList();
        if (where.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.Append(FoldConditions(spec, where, ref slot));
        }

        if (spec.GroupBy.Count > 0)
        {
            sql.Append(" GROUP BY ");
            sql.Append(string.Join(", ", spec.GroupBy.Select(g => Column(spec, g.Alias, g.DbName))));
        }

        if (spec.Having.Count > 0)
        {
            sql.Append(" HAVING ");
            sql.Append(FoldConditions(spec, spec.Having, ref slot));
        }

        if (spec.OrderBy.Count > 0)
        {
            sql.Append(" ORDER BY ");
            sql.Append(string.Join(
                ", ",
                spec.OrderBy.Select(o => o.Desc ? $"{Column(spec, o.Alias, o.DbName)} DESC" : Column(spec, o.Alias, o.DbName))));
        }

        if (spec.IsPostgres)
        {
            if (spec.Limit is not null)
            {
                sql.Append(" LIMIT ");
                sql.Append(spec.Limit.Value);
            }

            if (spec.Offset is not null)
            {
                sql.Append(" OFFSET ");
                sql.Append(spec.Offset.Value);
            }
        }
        else if (spec.Limit is not null || spec.Offset is not null)
        {
            sql.Append(" OFFSET ");
            sql.Append(spec.Offset ?? 0);
            sql.Append(" ROWS");
            if (spec.Limit is not null)
            {
                sql.Append(" FETCH NEXT ");
                sql.Append(spec.Limit.Value);
                sql.Append(" ROWS ONLY");
            }
        }

        foreach (var union in spec.UnionAll)
        {
            var body = Emit(union, ref slot, includeWith: false, mask);
            if (body is null)
            {
                return null;
            }

            sql.Append(" UNION ALL ");
            sql.Append(body);
        }

        return sql.ToString();
    }

    // Left-fold with the same parenthesization the runtime emitter produces
    // for nested And nodes: ((c1 AND c2) AND c3).
    private static string FoldConditions(BakedQuerySpec spec, IReadOnlyList<BakedCondition> conditions, ref int slot)
    {
        var result = Condition(spec, conditions[0], ref slot);
        for (var i = 1; i < conditions.Count; i++)
        {
            result = $"({result} AND {Condition(spec, conditions[i], ref slot)})";
        }

        return result;
    }

    private static string Placeholder(BakedQuerySpec spec, ref int slot)
    {
        var placeholder = spec.IsPostgres ? $"${slot + 1}" : $"@p{slot}";
        slot++;
        return placeholder;
    }

    // A rendered expression -- an IN list, a CASE -- carries one marker per value
    // it binds, in the order Parameterizer captures them. The walker cannot know
    // the slot numbers (they depend on everything emitted before it), so it leaves
    // markers and they are numbered here.
    internal const char BindMarker = '';

    private static string Substitute(BakedQuerySpec spec, string sql, ref int slot)
    {
        if (sql.IndexOf(BindMarker) < 0)
        {
            return sql;
        }

        var result = new StringBuilder(sql.Length);
        foreach (var character in sql)
        {
            result.Append(character == BindMarker ? Placeholder(spec, ref slot) : character.ToString());
        }

        return result.ToString();
    }

    private static string Condition(BakedQuerySpec spec, BakedCondition condition, ref int slot)
    {
        if (condition.Combinator is { } combinator)
        {
            var parts = new List<string>(condition.Children!.Count);
            foreach (var child in condition.Children)
            {
                parts.Add(Condition(spec, child, ref slot));
            }

            return "(" + string.Join($" {combinator} ", parts) + ")";
        }

        var left = condition.LeftExpression is { } leftExpression
            ? Substitute(spec, leftExpression, ref slot)
            : Column(spec, condition.LeftAlias, condition.LeftDbName);
        if (condition.IsUnary)
        {
            return $"{left} {condition.Op}";
        }

        if (condition.RightExpression is not null)
        {
            return $"{left} {condition.Op} {Substitute(spec, condition.RightExpression, ref slot)}";
        }

        if (!condition.IsBind)
        {
            return $"{left} {condition.Op} {Column(spec, condition.RightAlias!, condition.RightDbName!)}";
        }

        return $"{left} {condition.Op} {Placeholder(spec, ref slot)}";
    }

    private static string Table(BakedQuerySpec spec, BakedTable table)
    {
        var name = table.Facts.Schema is null
            ? Quote(spec, table.Facts.TableName)
            : $"{Quote(spec, table.Facts.Schema)}.{Quote(spec, table.Facts.TableName)}";
        return $"{name} AS {Quote(spec, table.Alias)}";
    }

    private static string SelectItem(BakedQuerySpec spec, BakedColumn column, ref int slot)
    {
        var expr = column.IsLiteral
            ? Placeholder(spec, ref slot)
            : column.SqlExpression is { } sqlExpression
                ? Substitute(spec, sqlExpression, ref slot)
                : Column(spec, column.TableAlias, column.DbName);
        return column.ProjectionName is null ? expr : $"{expr} AS {Quote(spec, column.ProjectionName)}";
    }

    private static string Column(BakedQuerySpec spec, string alias, string dbName)
        => $"{Quote(spec, alias)}.{Quote(spec, dbName)}";

    private static string Quote(BakedQuerySpec spec, string identifier)
        => spec.IsPostgres
            ? $"\"{identifier.Replace("\"", "\"\"")}\""
            : $"[{identifier.Replace("]", "]]")}]";
}

// Tables in a query plus the WHERE columns that actually constrain them.
// Nested holds UNION ALL branches, each checked independently.
internal sealed class AlwaysFilterQuery
{
    public AlwaysFilterQuery(
        IReadOnlyList<BakedTable> tables,
        IReadOnlyList<BakedCondition> where,
        IReadOnlyList<AlwaysFilterQuery>? nested = null)
    {
        Tables = tables;
        Where = where;
        Nested = nested ?? Array.Empty<AlwaysFilterQuery>();
    }

    public IReadOnlyList<BakedTable> Tables { get; }
    public IReadOnlyList<BakedCondition> Where { get; }
    public IReadOnlyList<AlwaysFilterQuery> Nested { get; }
}
