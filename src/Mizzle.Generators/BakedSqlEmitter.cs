using System.Text;

namespace Mizzle.Generators;

internal sealed class BakedQuerySpec
{
    public BakedQuerySpec(
        bool isPostgres,
        TableFactsModel table,
        IReadOnlyList<string> selectDbNames,
        bool distinct,
        string? whereDbName,
        IReadOnlyList<(string DbName, bool Desc)> orderBy,
        int? limit,
        int? offset)
    {
        IsPostgres = isPostgres;
        Table = table;
        SelectDbNames = selectDbNames;
        Distinct = distinct;
        WhereDbName = whereDbName;
        OrderBy = orderBy;
        Limit = limit;
        Offset = offset;
    }

    public bool IsPostgres { get; }
    public TableFactsModel Table { get; }
    public IReadOnlyList<string> SelectDbNames { get; }
    public bool Distinct { get; }
    public string? WhereDbName { get; }
    public IReadOnlyList<(string DbName, bool Desc)> OrderBy { get; }
    public int? Limit { get; }
    public int? Offset { get; }
}

internal static class BakedSqlEmitter
{
    // Mirrors PgEmitter/SqlServerEmitter output for the statically-visible subset.
    // Returns null when the shape cannot be baked (SQL Server paging without ORDER BY).
    public static string? Emit(BakedQuerySpec spec)
    {
        if (!spec.IsPostgres && (spec.Limit is not null || spec.Offset is not null) && spec.OrderBy.Count == 0)
        {
            return null;
        }

        var alias = spec.Table.Alias;
        var sql = new StringBuilder();
        sql.Append("SELECT ");
        if (spec.Distinct)
        {
            sql.Append("DISTINCT ");
        }

        sql.Append(string.Join(", ", spec.SelectDbNames.Select(c => Column(spec, alias, c))));
        sql.Append(" FROM ");
        if (spec.Table.Schema is not null)
        {
            sql.Append(Quote(spec, spec.Table.Schema));
            sql.Append('.');
        }

        sql.Append(Quote(spec, spec.Table.TableName));
        sql.Append(" AS ");
        sql.Append(Quote(spec, alias));
        if (spec.WhereDbName is not null)
        {
            sql.Append(" WHERE ");
            sql.Append(Column(spec, alias, spec.WhereDbName));
            sql.Append(spec.IsPostgres ? " = $1" : " = @p0");
        }

        if (spec.OrderBy.Count > 0)
        {
            sql.Append(" ORDER BY ");
            sql.Append(string.Join(
                ", ",
                spec.OrderBy.Select(o => o.Desc ? $"{Column(spec, alias, o.DbName)} DESC" : Column(spec, alias, o.DbName))));
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

    private static string Column(BakedQuerySpec spec, string alias, string dbName)
        => $"{Quote(spec, alias)}.{Quote(spec, dbName)}";

    private static string Quote(BakedQuerySpec spec, string identifier)
        => spec.IsPostgres
            ? $"\"{identifier.Replace("\"", "\"\"")}\""
            : $"[{identifier.Replace("]", "]]")}]";
}
