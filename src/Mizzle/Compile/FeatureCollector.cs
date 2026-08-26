using Mizzle.Ir;

namespace Mizzle.Compile;

public static class FeatureCollector
{
    public static IReadOnlyList<Feature> Collect(Query query)
    {
        if (query is LockQuery)
        {
            return [Feature.AdvisoryLock];
        }

        var features = new HashSet<Feature>();
        if (query is SelectQuery select)
        {
            CollectSelect(select, features);
        }
        else
        {
            CollectWrite(query, features);
        }

        return [..features];
    }

    private static void CollectSelect(SelectQuery select, HashSet<Feature> features)
    {
        if (select.WindowCount)
        {
            features.Add(Feature.WindowCount);
        }

        if (select.Limit is not null || select.Offset is not null)
        {
            features.Add(Feature.Limit);
        }

        if (select.RecursiveWith)
        {
            features.Add(Feature.RecursiveCte);
        }

        foreach (var item in select.Select)
        {
            CollectExpr(item.Expr, features);
        }

        if (select.Where is not null)
        {
            CollectExpr(select.Where, features);
        }

        foreach (var join in select.Joins)
        {
            CollectExpr(join.On, features);
        }

        if (select.GroupBy is not null)
        {
            foreach (var group in select.GroupBy)
            {
                CollectExpr(group, features);
            }
        }

        if (select.Having is not null)
        {
            CollectExpr(select.Having, features);
        }

        foreach (var order in select.OrderBy)
        {
            CollectExpr(order.Expr, features);
        }

        foreach (var cte in select.With)
        {
            CollectSelect(cte.Query, features);
        }

        foreach (var union in select.UnionAll)
        {
            CollectSelect(union, features);
        }
    }

    private static void CollectWrite(Query query, HashSet<Feature> features)
    {
        switch (query)
        {
            case InsertQuery insert:
                CollectWriteMeta(insert.With, insert.RecursiveWith, insert.Returning, features);
                if (insert.FromSelect is not null)
                {
                    CollectSelect(insert.FromSelect, features);
                }

                foreach (var row in insert.ValuesRows)
                {
                    foreach (var value in row)
                    {
                        CollectExpr(value, features);
                    }
                }

                break;
            case UpdateQuery update:
                CollectWriteMeta(update.With, update.RecursiveWith, update.Returning, features);
                foreach (var (_, value) in update.Set)
                {
                    CollectExpr(value, features);
                }

                if (update.Where is not null)
                {
                    CollectExpr(update.Where, features);
                }

                break;
            case DeleteQuery delete:
                CollectWriteMeta(delete.With, delete.RecursiveWith, delete.Returning, features);
                if (delete.Where is not null)
                {
                    CollectExpr(delete.Where, features);
                }

                break;
        }
    }

    private static void CollectWriteMeta(
        IReadOnlyList<CteClause> with,
        bool recursiveWith,
        IReadOnlyList<SelectItem> returning,
        HashSet<Feature> features)
    {
        if (with.Count > 0)
        {
            features.Add(Feature.DmlWithCte);
        }

        if (recursiveWith)
        {
            features.Add(Feature.RecursiveCte);
        }

        if (returning.Count > 0)
        {
            features.Add(Feature.Returning);
        }

        foreach (var item in returning)
        {
            CollectExpr(item.Expr, features);
        }

        foreach (var cte in with)
        {
            CollectSelect(cte.Query, features);
        }
    }

    private static void CollectExpr(Expr expr, HashSet<Feature> features)
    {
        switch (expr)
        {
            case BinaryExpr { Op: BinaryOp.ILike } ilike:
                features.Add(Feature.ILike);
                CollectExpr(ilike.Left, features);
                CollectExpr(ilike.Right, features);
                break;
            case BinaryExpr bin:
                CollectExpr(bin.Left, features);
                CollectExpr(bin.Right, features);
                break;
            case UnaryExpr unary:
                CollectExpr(unary.Operand, features);
                break;
            case InExpr inn:
                CollectExpr(inn.Needle, features);
                foreach (var item in inn.Haystack)
                {
                    CollectExpr(item, features);
                }

                break;
            case BetweenExpr between:
                CollectExpr(between.Value, features);
                CollectExpr(between.Lo, features);
                CollectExpr(between.Hi, features);
                break;
            case CoalesceExpr coalesce:
                foreach (var arg in coalesce.Args)
                {
                    CollectExpr(arg, features);
                }

                break;
            case AggregateExpr { Arg: not null } agg:
                CollectExpr(agg.Arg, features);
                break;
            case CallExpr call:
                foreach (var arg in call.Args)
                {
                    CollectExpr(arg, features);
                }

                break;
        }
    }
}
