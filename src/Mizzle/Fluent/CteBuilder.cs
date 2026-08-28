using Mizzle.Ir;

namespace Mizzle.Fluent;

/// <summary>Builds the common table expressions passed to <c>With</c> and <c>WithRecursive</c>.</summary>
public static class CteBuilder
{
    /// <summary>Names a select for use as a common table expression.</summary>
    /// <param name="name">The CTE's name. A string literal keeps the query on the baked path.</param>
    /// <param name="query">The CTE body.</param>
    public static CteClause Named(string name, SelectQuery query) => new(name, query);
}
