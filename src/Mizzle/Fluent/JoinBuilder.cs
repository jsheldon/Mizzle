using Mizzle.Ir;

namespace Mizzle.Fluent;

// Intermediate returned by SelectBuilder.InnerJoin(table)/LeftJoin(table);
// On(...) AND-combines the conditions and returns the select builder.
public sealed class JoinBuilder
{
    private readonly SelectBuilder _parent;
    private readonly JoinKind _kind;
    private readonly FromSource _target;

    internal JoinBuilder(SelectBuilder parent, JoinKind kind, FromSource target)
    {
        _parent = parent;
        _kind = kind;
        _target = target;
    }

    public SelectBuilder On(params Expr[] conditions)
    {
        var on = conditions.Length switch
        {
            0 => throw new ArgumentException("At least one join condition is required.", nameof(conditions)),
            1 => conditions[0],
            _ => Sql.And(conditions)
        };
        return _kind == JoinKind.Inner ? _parent.InnerJoin(_target, on) : _parent.LeftJoin(_target, on);
    }
}
