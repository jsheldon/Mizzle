using Mizzle.Ir;

namespace Mizzle.Compile;

// Replaces every ValueExpr with a ParamRef in a deterministic walk order and
// collects the values in slot order. The canonical query is the shape-cache
// key; the values bind per execution. The walk order must match placeholder
// numbering assumptions in the emitters and the baked-SQL generator:
// select: With CTEs -> select items -> joins (On, in order) -> where ->
// group by -> having -> order by -> union-all members;
// insert: With -> values rows (row-major) -> from-select -> returning;
// update: With -> set values -> where -> returning;
// delete: With -> where -> returning.
public static class Parameterizer
{
    public static (Query Canonical, IReadOnlyList<object?> Values) Run(Query query)
    {
        var pass = new Pass();
        return (pass.Rewrite(query), pass.Values);
    }

    private sealed class Pass
    {
        public List<object?> Values { get; } = [];

        public Query Rewrite(Query query) => query switch
        {
            SelectQuery select => RewriteSelect(select),
            InsertQuery insert => insert with
            {
                With = RewriteCtes(insert.With),
                ValuesRows = [..insert.ValuesRows.Select(row => new EquatableList<Expr>(row.Select(Rewrite)))],
                FromSelect = insert.FromSelect is null ? null : RewriteSelect(insert.FromSelect),
                Returning = RewriteItems(insert.Returning)
            },
            UpdateQuery update => update with
            {
                With = RewriteCtes(update.With),
                Set = [..update.Set.Select(s => (s.Column, Rewrite(s.Value)))],
                Where = update.Where is null ? null : Rewrite(update.Where),
                Returning = RewriteItems(update.Returning)
            },
            DeleteQuery delete => delete with
            {
                With = RewriteCtes(delete.With),
                Where = delete.Where is null ? null : Rewrite(delete.Where),
                Returning = RewriteItems(delete.Returning)
            },
            _ => query
        };

        private SelectQuery RewriteSelect(SelectQuery select) => select with
        {
            With = RewriteCtes(select.With),
            Select = RewriteItems(select.Select),
            Joins = [..select.Joins.Select(j => j with { On = Rewrite(j.On) })],
            Where = select.Where is null ? null : Rewrite(select.Where),
            GroupBy = select.GroupBy is null ? null : new EquatableList<Expr>(select.GroupBy.Select(Rewrite)),
            Having = select.Having is null ? null : Rewrite(select.Having),
            OrderBy = [..select.OrderBy.Select(o => o with { Expr = Rewrite(o.Expr) })],
            UnionAll = [..select.UnionAll.Select(RewriteSelect)]
        };

        private EquatableList<CteClause> RewriteCtes(EquatableList<CteClause> with)
            => [..with.Select(cte => cte with { Query = RewriteSelect(cte.Query) })];

        private EquatableList<SelectItem> RewriteItems(EquatableList<SelectItem> items)
            => [..items.Select(item => item with { Expr = Rewrite(item.Expr) })];

        private Expr Rewrite(Expr expr) => expr switch
        {
            ValueExpr value => Capture(value),
            ParamRef => throw new InvalidOperationException(
                "Query already contains parameter slots; parameterization must run exactly once."),
            ColumnRef => expr,
            BinaryExpr bin => bin with { Left = Rewrite(bin.Left), Right = Rewrite(bin.Right) },
            UnaryExpr unary => unary with { Operand = Rewrite(unary.Operand) },
            InExpr inn => inn with
            {
                Needle = Rewrite(inn.Needle),
                Haystack = [..inn.Haystack.Select(Rewrite)]
            },
            BetweenExpr between => between with
            {
                Value = Rewrite(between.Value),
                Lo = Rewrite(between.Lo),
                Hi = Rewrite(between.Hi)
            },
            CoalesceExpr coalesce => coalesce with { Args = [..coalesce.Args.Select(Rewrite)] },
            AggregateExpr agg => agg with { Arg = agg.Arg is null ? null : Rewrite(agg.Arg) },
            CallExpr call => call with { Args = [..call.Args.Select(Rewrite)] },
            _ => expr
        };

        private ParamRef Capture(ValueExpr value)
        {
            var slot = Values.Count;
            Values.Add(value.Value);
            return new ParamRef(slot, value.ClrType);
        }
    }
}
