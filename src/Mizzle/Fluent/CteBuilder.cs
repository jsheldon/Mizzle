using Mizzle.Ir;

namespace Mizzle.Fluent;

public static class CteBuilder
{
    public static CteClause Named(string name, SelectQuery query) => new(name, query);
}
