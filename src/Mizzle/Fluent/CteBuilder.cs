using Mizzle.Ir;

namespace Mizzle.Fluent;

/// <summary>Builds the common table expressions passed to <c>With</c> and <c>WithRecursive</c>.</summary>
public static class CteBuilder
{
    /// <summary>Names a select for use as a common table expression.</summary>
    /// <param name="name">The CTE's name. A string literal keeps the query on the baked path.</param>
    /// <param name="query">The CTE body.</param>
    public static CteClause Named(string name, SelectQuery query) => new(name, query);

    /// <summary>
    ///     Names a select for use as a common table expression, and declares
    ///     <typeparamref name="T"/> as a table type with one typed column per
    ///     column the body projects -- construct it with <c>new T()</c> and use
    ///     it in <c>From</c>/joins/<c>Select</c> like any other table, instead of
    ///     hand-declaring a table whose columns must be kept in sync by hand.
    ///     <typeparamref name="T"/> must not already exist; the generator declares
    ///     it. Requires <paramref name="name"/> to be a string literal and
    ///     <paramref name="query"/> to be a statically visible chain, the same
    ///     requirements the baked path already has.
    /// </summary>
    /// <example>
    ///     <code>
    ///     var body = db.Select(o.Ndc, Sql.As(Sql.RowNumber()...OrderByDesc(o.EffectiveDate), "rn")).From(o).Build();
    ///     var cte = CteBuilder.Named&lt;Ranked&gt;("ranked", body);
    ///     var ranked = new Ranked();
    ///     var rows = await db.Select(ranked.Ndc).With(cte).From(ranked).Where(ranked.Rn.Eq(1)).ToListAsync&lt;BestNdc&gt;();
    ///     </code>
    /// </example>
    public static CteClause Named<T>(string name, SelectQuery query) => new(name, query);
}
