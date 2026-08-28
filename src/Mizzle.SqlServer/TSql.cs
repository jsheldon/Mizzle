using Mizzle.Ir;
using Mizzle.Schema;

namespace Mizzle.SqlServer;

public static class TSql
{
    public static CallExpr Len(Expr value) => new("len", [value], DialectKind.SqlServer);

    public static CallExpr GetUtcDate() => new("getutcdate", [], DialectKind.SqlServer);

    /// <summary>Current local date and time, as <c>GETDATE()</c>.</summary>
    public static CallExpr GetDate() => new("getdate", [], DialectKind.SqlServer);

    /// <summary>Strips trailing whitespace, as <c>RTRIM(...)</c>.</summary>
    public static CallExpr RTrim(Expr value) => new("rtrim", [value], DialectKind.SqlServer);

    /// <summary>Strips trailing whitespace from a column.</summary>
    public static CallExpr RTrim(IColumn column) => RTrim(column.ToRef());

    /// <summary>Strips leading whitespace, as <c>LTRIM(...)</c>.</summary>
    public static CallExpr LTrim(Expr value) => new("ltrim", [value], DialectKind.SqlServer);

    /// <summary>Uppercases a value, as <c>UPPER(...)</c>.</summary>
    public static CallExpr Upper(Expr value) => new("upper", [value], DialectKind.SqlServer);

    /// <summary>Lowercases a value, as <c>LOWER(...)</c>.</summary>
    public static CallExpr Lower(Expr value) => new("lower", [value], DialectKind.SqlServer);
}
