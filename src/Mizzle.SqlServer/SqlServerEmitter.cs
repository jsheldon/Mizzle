namespace Mizzle.SqlServer;

using System.Text;
using Mizzle.Compile;
using Mizzle.Ir;

public sealed class SqlServerEmitter : ISqlEmitter
{
    public CompiledSql Emit(Query query, ParamBag parameters)
    {
        CapabilityChecker.Check(
            EmitterFeatures.For(DialectKind.SqlServer, FeatureCollector.Collect(query)),
            SqlServerCapabilities.Instance);

        var sql = new StringBuilder();
        switch (query)
        {
            case SelectQuery select:
                WriteSelect(sql, select, includeWith: true);
                break;
            case InsertQuery insert:
                WriteInsert(sql, insert);
                break;
            case UpdateQuery update:
                WriteUpdate(sql, update);
                break;
            case DeleteQuery delete:
                WriteDelete(sql, delete);
                break;
            case LockQuery:
                sql.Append("EXEC sp_getapplock @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Transaction';");
                break;
            default:
                throw new NotSupportedException($"SQL Server emitter does not support {query.GetType().Name} yet.");
        }

        return new CompiledSql(sql.ToString(), parameters.Values);
    }

    private static void WriteInsert(StringBuilder sql, InsertQuery insert)
    {
        sql.Append("INSERT INTO ");
        sql.Append(Table(insert.Into));
        sql.Append(" (");
        sql.Append(string.Join(", ", insert.Columns.Select(Quote)));
        sql.Append(')');
        if (insert.Returning.Count > 0)
        {
            sql.Append(" OUTPUT ");
            sql.Append(string.Join(", ", insert.Returning.Select(i => OutputItem(i, "INSERTED"))));
        }

        sql.Append(" VALUES ");
        sql.Append(string.Join(", ", insert.ValuesRows.Select(row => $"({string.Join(", ", row.Select(Expr))})")));
    }

    private static void WriteUpdate(StringBuilder sql, UpdateQuery update)
    {
        sql.Append("UPDATE ");
        sql.Append(Table(update.Table));
        sql.Append(" SET ");
        sql.Append(string.Join(", ", update.Set.Select(s => $"{Quote(s.Column)} = {Expr(s.Value)}")));
        if (update.Returning.Count > 0)
        {
            sql.Append(" OUTPUT ");
            sql.Append(string.Join(", ", update.Returning.Select(i => OutputItem(i, "INSERTED"))));
        }

        if (update.Where is not null)
        {
            sql.Append(" WHERE ");
            sql.Append(Expr(update.Where));
        }
    }

    private static void WriteDelete(StringBuilder sql, DeleteQuery delete)
    {
        sql.Append("DELETE FROM ");
        sql.Append(Table(delete.From));
        if (delete.Returning.Count > 0)
        {
            sql.Append(" OUTPUT ");
            sql.Append(string.Join(", ", delete.Returning.Select(i => OutputItem(i, "DELETED"))));
        }

        if (delete.Where is not null)
        {
            sql.Append(" WHERE ");
            sql.Append(Expr(delete.Where));
        }
    }

    private static string OutputItem(SelectItem item, string source)
    {
        if (item.Expr is not ColumnRef column)
        {
            throw new NotSupportedException("SQL Server OUTPUT requires a column reference.");
        }

        return $"{source}.{Quote(column.ColumnName)}";
    }

    private static string Table(FromSource from)
        => from.Schema is null ? Quote(from.TableName) : $"{Quote(from.Schema)}.{Quote(from.TableName)}";

    private static void WriteSelect(StringBuilder sql, SelectQuery select, bool includeWith)
    {
        if (includeWith && select.With.Count > 0)
        {
            sql.Append("WITH ");
            for (var i = 0; i < select.With.Count; i++)
            {
                if (i > 0)
                {
                    sql.Append(", ");
                }

                var cte = select.With[i];
                sql.Append(Quote(cte.Name));
                sql.Append(" AS (");
                WriteSelect(sql, cte.Query, includeWith: false);
                sql.Append(')');
            }

            sql.Append(' ');
        }

        WriteSelectCore(sql, select);
        foreach (var union in select.UnionAll)
        {
            sql.Append(" UNION ALL ");
            WriteSelectCore(sql, union);
        }
    }

    private static void WriteSelectCore(StringBuilder sql, SelectQuery select)
    {
        sql.Append("SELECT ");
        if (select.Distinct)
        {
            sql.Append("DISTINCT ");
        }

        sql.Append(string.Join(", ", select.Select.Select(Item)));
        if (select.WindowCount)
        {
            sql.Append(", count(*) OVER() AS ");
            sql.Append(Quote("mizzle_total"));
        }

        sql.Append(" FROM ");
        sql.Append(From(select.From));
        foreach (var join in select.Joins)
        {
            sql.Append(join.Kind == JoinKind.Inner ? " INNER JOIN " : " LEFT JOIN ");
            sql.Append(From(join.Target));
            sql.Append(" ON ");
            sql.Append(Expr(join.On));
        }

        if (select.Where is not null)
        {
            sql.Append(" WHERE ");
            sql.Append(Expr(select.Where));
        }

        if (select.GroupBy is { Count: > 0 })
        {
            sql.Append(" GROUP BY ");
            sql.Append(string.Join(", ", select.GroupBy.Select(Expr)));
        }

        if (select.Having is not null)
        {
            sql.Append(" HAVING ");
            sql.Append(Expr(select.Having));
        }

        if (select.OrderBy.Count > 0)
        {
            sql.Append(" ORDER BY ");
            sql.Append(string.Join(", ", select.OrderBy.Select(Order)));
        }

        if (select.Limit is not null || select.Offset is not null)
        {
            sql.Append(" OFFSET ");
            sql.Append(select.Offset ?? 0);
            sql.Append(" ROWS FETCH NEXT ");
            sql.Append(select.Limit ?? 0);
            sql.Append(" ROWS ONLY");
        }
    }

    private static string Item(SelectItem item)
    {
        var expr = Expr(item.Expr);
        return item.Alias is null ? expr : $"{expr} AS {Quote(item.Alias)}";
    }

    private static string From(FromSource from)
    {
        var table = from.Schema is null
            ? Quote(from.TableName)
            : $"{Quote(from.Schema)}.{Quote(from.TableName)}";
        return $"{table} AS {Quote(from.Alias)}";
    }

    private static string Order(OrderByItem item)
        => item.Descending ? $"{Expr(item.Expr)} DESC" : Expr(item.Expr);

    private static string Expr(Expr expr) => expr switch
    {
        ColumnRef col => $"{Quote(col.TableAlias)}.{Quote(col.ColumnName)}",
        ParamRef param => $"@p{param.Slot}",
        BinaryExpr bin => Binary(bin),
        UnaryExpr unary => Unary(unary),
        AggregateExpr agg => Aggregate(agg),
        CallExpr call => Call(call),
        InExpr inn => $"{Expr(inn.Needle)} IN ({string.Join(", ", inn.Haystack.Select(Expr))})",
        BetweenExpr b => $"{Expr(b.Value)} BETWEEN {Expr(b.Lo)} AND {Expr(b.Hi)}",
        CoalesceExpr c => $"coalesce({string.Join(", ", c.Args.Select(Expr))})",
        _ => throw new NotSupportedException($"Unsupported expression {expr.GetType().Name}.")
    };

    private static string Call(CallExpr call)
    {
        if (call.Dialect != DialectKind.SqlServer)
        {
            throw new InvalidOperationException($"Function '{call.Name}' is not valid on {DialectKind.SqlServer}");
        }

        return $"{call.Name}({string.Join(", ", call.Args.Select(Expr))})";
    }

    private static string Aggregate(AggregateExpr agg)
    {
        var name = agg.Kind.ToString().ToLowerInvariant();
        return agg.Arg is null ? $"{name}(*)" : $"{name}({Expr(agg.Arg)})";
    }

    private static string Binary(BinaryExpr bin)
    {
        var op = bin.Op switch
        {
            BinaryOp.Eq => "=",
            BinaryOp.Ne => "<>",
            BinaryOp.Gt => ">",
            BinaryOp.Gte => ">=",
            BinaryOp.Lt => "<",
            BinaryOp.Lte => "<=",
            BinaryOp.And => "AND",
            BinaryOp.Or => "OR",
            BinaryOp.Like => "LIKE",
            BinaryOp.ILike => "ILIKE",
            _ => throw new NotSupportedException($"Unsupported operator {bin.Op}.")
        };

        return bin.Op is BinaryOp.And or BinaryOp.Or
            ? $"({Expr(bin.Left)} {op} {Expr(bin.Right)})"
            : $"{Expr(bin.Left)} {op} {Expr(bin.Right)}";
    }

    private static string Unary(UnaryExpr unary) => unary.Op switch
    {
        UnaryOp.Not => $"NOT ({Expr(unary.Operand)})",
        UnaryOp.IsNull => $"{Expr(unary.Operand)} IS NULL",
        UnaryOp.IsNotNull => $"{Expr(unary.Operand)} IS NOT NULL",
        _ => throw new NotSupportedException($"Unsupported unary operator {unary.Op}.")
    };

    internal static string Quote(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}
