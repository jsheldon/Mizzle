using System.Text;

namespace Mizzle.Generators;

internal sealed class BakedColumn
{
    public BakedColumn(string tableAlias, string dbName, string propertyName, string clrTypeName, bool isRequired, string readerCall, string? readConverter = null, string? projectionName = null, bool isUntrimmed = false)
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
    public BakedCondition(string leftAlias, string leftDbName, string? rightAlias, string? rightDbName)
    {
        LeftAlias = leftAlias;
        LeftDbName = leftDbName;
        RightAlias = rightAlias;
        RightDbName = rightDbName;
    }

    public string LeftAlias { get; }
    public string LeftDbName { get; }
    public string? RightAlias { get; }
    public string? RightDbName { get; }

    public bool IsBind => RightAlias is null;
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
        bool recursiveWith)
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
}

// Mirrors PgEmitter/SqlServerEmitter output for the statically-visible subset,
// with bind slots numbered in Parameterizer order: join conditions (join order,
// condition order), then where conditions. Returns null when the shape cannot
// be baked (SQL Server paging without ORDER BY).
internal static class BakedSqlEmitter
{
    public static string? Emit(BakedQuerySpec spec)
    {
        var slot = 0;
        return Emit(spec, ref slot, includeWith: true);
    }

    // Parameterizer order for a select is: With CTEs -> select items -> joins ->
    // where, so a CTE body's binds must take the lowest slots.
    private static string? Emit(BakedQuerySpec spec, ref int slot, bool includeWith)
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
                var body = Emit(spec.With[i].Body, ref slot, includeWith: false);
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

        sql.Append(string.Join(", ", spec.Select.Select(c => SelectItem(spec, c))));
        sql.Append(" FROM ");
        sql.Append(Table(spec, spec.From));
        foreach (var join in spec.Joins)
        {
            sql.Append(join.IsLeft ? " LEFT JOIN " : " INNER JOIN ");
            sql.Append(Table(spec, join.Table));
            sql.Append(" ON ");
            sql.Append(FoldConditions(spec, join.On, ref slot));
        }

        if (spec.Where.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.Append(FoldConditions(spec, spec.Where, ref slot));
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

    private static string Condition(BakedQuerySpec spec, BakedCondition condition, ref int slot)
    {
        var left = Column(spec, condition.LeftAlias, condition.LeftDbName);
        if (!condition.IsBind)
        {
            return $"{left} = {Column(spec, condition.RightAlias!, condition.RightDbName!)}";
        }

        var placeholder = spec.IsPostgres ? $"${slot + 1}" : $"@p{slot}";
        slot++;
        return $"{left} = {placeholder}";
    }

    private static string Table(BakedQuerySpec spec, BakedTable table)
    {
        var name = table.Facts.Schema is null
            ? Quote(spec, table.Facts.TableName)
            : $"{Quote(spec, table.Facts.Schema)}.{Quote(spec, table.Facts.TableName)}";
        return $"{name} AS {Quote(spec, table.Alias)}";
    }

    private static string SelectItem(BakedQuerySpec spec, BakedColumn column)
    {
        var expr = Column(spec, column.TableAlias, column.DbName);
        return column.ProjectionName is null ? expr : $"{expr} AS {Quote(spec, column.ProjectionName)}";
    }

    private static string Column(BakedQuerySpec spec, string alias, string dbName)
        => $"{Quote(spec, alias)}.{Quote(spec, dbName)}";

    private static string Quote(BakedQuerySpec spec, string identifier)
        => spec.IsPostgres
            ? $"\"{identifier.Replace("\"", "\"\"")}\""
            : $"[{identifier.Replace("]", "]]")}]";
}
