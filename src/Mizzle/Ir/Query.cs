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
    EquatableList<SelectItem> Select,
    FromSource From,
    EquatableList<JoinClause> Joins,
    Expr? Where,
    EquatableList<OrderByItem> OrderBy,
    int? Limit,
    int? Offset,
    bool Distinct,
    EquatableList<CteClause> With,
    bool RecursiveWith,
    EquatableList<SelectQuery> UnionAll,
    EquatableList<Expr>? GroupBy = null,
    Expr? Having = null,
    bool WindowCount = false) : Query;

public sealed record InsertQuery(
    FromSource Into,
    EquatableList<string> Columns,
    EquatableList<EquatableList<Expr>> ValuesRows,
    SelectQuery? FromSelect,
    EquatableList<SelectItem> Returning,
    EquatableList<CteClause> With,
    bool RecursiveWith) : Query;

public sealed record UpdateQuery(
    FromSource Table,
    EquatableList<(string Column, Expr Value)> Set,
    Expr? Where,
    EquatableList<SelectItem> Returning,
    EquatableList<CteClause> With,
    bool RecursiveWith) : Query;

public sealed record DeleteQuery(
    FromSource From,
    Expr? Where,
    EquatableList<SelectItem> Returning,
    EquatableList<CteClause> With,
    bool RecursiveWith) : Query;

public sealed record LockQuery(string Resource) : Query;
