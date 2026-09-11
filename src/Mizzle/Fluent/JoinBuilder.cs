using Mizzle.Ir;

namespace Mizzle.Fluent;

// Intermediate returned by SelectBuilder.InnerJoin(table)/LeftJoin(table);
// On(...) AND-combines the conditions and returns the select builder.
public sealed class JoinBuilder
{
    private readonly SelectBuilder _selectBuilder;
    private readonly JoinKind _joinKind;
    private readonly FromSource _joinTarget;

    internal JoinBuilder(SelectBuilder parent, JoinKind kind, FromSource target)
    {
        _selectBuilder = parent;
        _joinKind = kind;
        _joinTarget = target;
    }

    public SelectBuilder On(params Expr[] conditions)
    {
        var on = conditions.Length switch
        {
            0 => throw new ArgumentException("At least one join condition is required.", nameof(conditions)),
            1 => conditions[0],
            _ => Sql.And(conditions)
        };
        return _joinKind == JoinKind.Inner ? _selectBuilder.InnerJoin(_joinTarget, on) : _selectBuilder.LeftJoin(_joinTarget, on);
    }
}
