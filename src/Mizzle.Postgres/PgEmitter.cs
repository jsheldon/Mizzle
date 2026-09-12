namespace Mizzle.Postgres;

using System.Text;
using Mizzle.Compile;
using Mizzle.Ir;

public sealed class PgEmitter : ISqlEmitter
{
    public CompiledSql Emit(Query query, IReadOnlyList<object?> values)
    {
        CapabilityChecker.Check(
            EmitterFeatures.For(DialectKind.Postgres, FeatureCollector.Collect(query)),
            PgCapabilities.Instance);

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
                sql.Append("SELECT pg_advisory_xact_lock(hashtext($1))");
                break;
            default:
                throw new NotSupportedException($"Postgres emitter does not support {query.GetType().Name} yet.");
        }

        return new CompiledSql(sql.ToString(), values);
    }

    private static void WriteWithPrefix(StringBuilder sql, IReadOnlyList<CteClause> with, bool recursiveWith)
    {
        if (with.Count == 0)
        {
            return;
        }

        sql.Append(recursiveWith ? "WITH RECURSIVE " : "WITH ");
        for (var i = 0; i < with.Count; i++)
        {
            if (i > 0)
            {
                sql.Append(", ");
            }

            sql.Append(Quote(with[i].Name));
            sql.Append(" AS (");
            WriteSelect(sql, with[i].Query, includeWith: false);
            sql.Append(')');
        }

        sql.Append(' ');
    }

    private static void WriteInsert(StringBuilder sql, InsertQuery insert)
    {
        if (insert.ValuesRows.Count > 0 == insert.FromSelect is not null)
        {
            throw new InvalidOperationException("Insert requires exactly one of VALUES or a source select.");
        }

        WriteWithPrefix(sql, insert.With, insert.RecursiveWith);
        sql.Append("INSERT INTO ");
        sql.Append(Table(insert.Into));
        sql.Append(" (");
        sql.Append(string.Join(", ", insert.Columns.Select(Quote)));
        sql.Append(')');
        if (insert.FromSelect is not null)
        {
            sql.Append(' ');
            WriteSelect(sql, insert.FromSelect, includeWith: false);
        }
        else
        {
            sql.Append(" VALUES ");
            sql.Append(string.Join(", ", insert.ValuesRows.Select(row => $"({string.Join(", ", row.Select(Expr))})")));
        }

        if (insert.Returning.Count > 0)
        {
            sql.Append(" RETURNING ");
            sql.Append(string.Join(", ", insert.Returning.Select(Item)));
        }
    }

    private static void WriteUpdate(StringBuilder sql, UpdateQuery update)
    {
        WriteWithPrefix(sql, update.With, update.RecursiveWith);
        sql.Append("UPDATE ");
        sql.Append(Table(update.Table));
        sql.Append(" AS ");
        sql.Append(Quote(update.Table.Alias));
        sql.Append(" SET ");
        sql.Append(string.Join(", ", update.Set.Select(s => $"{Quote(s.Column)} = {Expr(s.Value)}")));
        if (update.Where is not null)
        {
            sql.Append(" WHERE ");
            sql.Append(Expr(update.Where));
        }

        if (update.Returning.Count > 0)
        {
            sql.Append(" RETURNING ");
            sql.Append(string.Join(", ", update.Returning.Select(Item)));
        }
    }

    private static void WriteDelete(StringBuilder sql, DeleteQuery delete)
    {
        WriteWithPrefix(sql, delete.With, delete.RecursiveWith);
        sql.Append("DELETE FROM ");
        sql.Append(Table(delete.From));
        sql.Append(" AS ");
        sql.Append(Quote(delete.From.Alias));
        if (delete.Where is not null)
        {
            sql.Append(" WHERE ");
            sql.Append(Expr(delete.Where));
        }

        if (delete.Returning.Count > 0)
        {
            sql.Append(" RETURNING ");
            sql.Append(string.Join(", ", delete.Returning.Select(Item)));
        }
    }

    private static string Table(FromSource from)
        => from.Schema is null ? Quote(from.TableName) : $"{Quote(from.Schema)}.{Quote(from.TableName)}";

    private static void WriteSelect(StringBuilder sql, SelectQuery select, bool includeWith)
    {
        if (includeWith)
        {
            WriteWithPrefix(sql, select.With, select.RecursiveWith);
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

        if (select.Limit is not null)
        {
            sql.Append(" LIMIT ");
            sql.Append(select.Limit.Value);
        }

        if (select.Offset is not null)
        {
            sql.Append(" OFFSET ");
            sql.Append(select.Offset.Value);
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
        ParamRef param => $"${param.Slot + 1}",
        ValueExpr => throw new InvalidOperationException("Query was not parameterized before emit."),
        BinaryExpr binary => Binary(binary),
        LikeExpr like => Like(like),
        UnaryExpr unary => Unary(unary),
        AggregateExpr aggregate => Aggregate(aggregate),
        CallExpr call => Call(call),
        InExpr inExpression => $"{Expr(inExpression.Needle)} IN ({string.Join(", ", inExpression.Haystack.Select(Expr))})",
        BetweenExpr b => $"{Expr(b.Value)} BETWEEN {Expr(b.Lo)} AND {Expr(b.Hi)}",
        CoalesceExpr c => $"coalesce({string.Join(", ", c.Args.Select(Expr))})",
        CaseExpr @case => Case(@case),
        RowNumberExpr rowNumber => RowNumber(rowNumber),
        NextValueExpr next => $"nextval('{next.Sequence.Replace("'", "''", StringComparison.Ordinal)}')",
        _ => throw new NotSupportedException($"Unsupported expression {expr.GetType().Name}.")
    };

    private static string Case(CaseExpr @case)
    {
        var sql = new StringBuilder("CASE");
        foreach (var when in @case.Whens)
        {
            sql.Append(" WHEN ").Append(Expr(when.Condition)).Append(" THEN ").Append(Expr(when.Result));
        }

        if (@case.Fallback is not null)
        {
            sql.Append(" ELSE ").Append(Expr(@case.Fallback));
        }

        return sql.Append(" END").ToString();
    }

    private static string RowNumber(RowNumberExpr rowNumber)
    {
        if (rowNumber.OrderColumns.Count == 0)
        {
            throw new InvalidOperationException("ROW_NUMBER() requires at least one ORDER BY column.");
        }

        var sql = new StringBuilder("ROW_NUMBER() OVER (");
        if (rowNumber.PartitionColumns.Count > 0)
        {
            sql.Append("PARTITION BY ")
                .Append(string.Join(", ", rowNumber.PartitionColumns.Select(Expr)))
                .Append(' ');
        }

        sql.Append("ORDER BY ").Append(string.Join(", ", rowNumber.OrderColumns.Select(Order)));
        return sql.Append(')').ToString();
    }

    private static string Call(CallExpr call)
    {
        if (call.Dialect != DialectKind.Postgres)
        {
            throw new InvalidOperationException($"Function '{call.Name}' is not valid on {DialectKind.Postgres}");
        }

        return $"{call.Name}({string.Join(", ", call.Args.Select(Expr))})";
    }

    private static string Aggregate(AggregateExpr aggregate)
    {
        var name = aggregate.Kind.ToString().ToLowerInvariant();
        var sql = aggregate.Arg is null ? $"{name}(*)" : $"{name}({Expr(aggregate.Arg)})";

        // SUM(real) stays real (single precision) on Postgres, but SQL Server's
        // SUM(real) widens to float (double precision) natively. Casting here
        // matches Sql.Sum's double-typed result on both dialects.
        if (aggregate.Kind == AggregateKind.Sum && aggregate.ArgClrType == typeof(float))
        {
            return $"CAST({sql} AS DOUBLE PRECISION)";
        }

        return sql;
    }

    private static string Like(LikeExpr like)
    {
        var op = like.CaseInsensitive ? "ILIKE" : "LIKE";
        var escapeLiteral = like.Escape == '\'' ? "''" : like.Escape.ToString();
        return $"{Expr(like.Left)} {op} {Expr(like.Right)} ESCAPE '{escapeLiteral}'";
    }

    private static string Binary(BinaryExpr binary)
    {
        var op = binary.Op switch
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
            BinaryOp.Add => "+",
            BinaryOp.Subtract => "-",
            _ => throw new NotSupportedException($"Unsupported operator {binary.Op}.")
        };

        return binary.Op is BinaryOp.And or BinaryOp.Or
            ? $"({Expr(binary.Left)} {op} {Expr(binary.Right)})"
            : $"{Expr(binary.Left)} {op} {Expr(binary.Right)}";
    }

    private static string Unary(UnaryExpr unary) => unary.Op switch
    {
        UnaryOp.Not => $"NOT ({Expr(unary.Operand)})",
        UnaryOp.IsNull => $"{Expr(unary.Operand)} IS NULL",
        UnaryOp.IsNotNull => $"{Expr(unary.Operand)} IS NOT NULL",
        _ => throw new NotSupportedException($"Unsupported unary operator {unary.Op}.")
    };

    internal static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
