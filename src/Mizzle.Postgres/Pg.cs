using Mizzle.Ir;
using Mizzle.Schema;

namespace Mizzle.Postgres;

public static class Pg
{
    public static CallExpr Lower(Expr value) => new("lower", [value], DialectKind.Postgres);

    public static CallExpr Now() => new("now", [], DialectKind.Postgres);

    /// <summary>Strips trailing whitespace, as <c>rtrim(...)</c>.</summary>
    public static CallExpr RTrim(Expr value) => new("rtrim", [value], DialectKind.Postgres);

    /// <summary>Strips trailing whitespace from a column.</summary>
    public static CallExpr RTrim(IColumn column) => RTrim(column.ToRef());

    /// <summary>Strips leading whitespace, as <c>ltrim(...)</c>.</summary>
    public static CallExpr LTrim(Expr value) => new("ltrim", [value], DialectKind.Postgres);

    /// <summary>Uppercases a value, as <c>upper(...)</c>.</summary>
    public static CallExpr Upper(Expr value) => new("upper", [value], DialectKind.Postgres);
}
