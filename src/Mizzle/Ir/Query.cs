namespace Mizzle.Ir;

public abstract record Query;

public sealed record SelectItem(Expr Expr, string? Alias);

public sealed record FromSource(string TableName, string? Schema, string Alias);

public sealed record JoinClause(JoinKind Kind, FromSource Target, Expr On);

public enum JoinKind
{
    Inner,
    Left
}

public sealed record OrderByItem(Expr Expr, bool Descending);

public sealed record CteClause(string Name, SelectQuery Query);

public sealed record SelectQuery(
    IReadOnlyList<SelectItem> Select,
    FromSource From,
    IReadOnlyList<JoinClause> Joins,
    Expr? Where,
    IReadOnlyList<OrderByItem> OrderBy,
    int? Limit,
    int? Offset,
    bool Distinct,
    IReadOnlyList<CteClause> With,
    bool RecursiveWith,
    IReadOnlyList<SelectQuery> UnionAll,
    IReadOnlyList<Expr>? GroupBy = null,
    Expr? Having = null,
    bool WindowCount = false) : Query;

public sealed record InsertQuery(
    FromSource Into,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<Expr>> ValuesRows,
    SelectQuery? FromSelect,
    IReadOnlyList<SelectItem> Returning,
    IReadOnlyList<CteClause> With,
    bool RecursiveWith) : Query;

public sealed record UpdateQuery(
    FromSource Table,
    IReadOnlyList<(string Column, Expr Value)> Set,
    Expr? Where,
    IReadOnlyList<SelectItem> Returning,
    IReadOnlyList<CteClause> With,
    bool RecursiveWith) : Query;

public sealed record DeleteQuery(
    FromSource From,
    Expr? Where,
    IReadOnlyList<SelectItem> Returning,
    IReadOnlyList<CteClause> With,
    bool RecursiveWith) : Query;

public sealed record LockQuery(string Resource) : Query;
