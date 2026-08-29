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

    /// <summary>
    ///     Converts <paramref name="value"/> to <paramref name="type"/>, as
    ///     <c>CONVERT(varchar(20), ...)</c>. Nested calls compose.
    /// </summary>
    public static ConvertExpr Convert(SqlType type, Expr value) => new(TypeName(type), value);

    /// <summary>Converts a column, as <c>CONVERT(int, [t].[col])</c>.</summary>
    public static ConvertExpr Convert(SqlType type, IColumn column) => Convert(type, column.ToRef());

    /// <summary>
    ///     Converts with a T-SQL style code, as
    ///     <c>CONVERT(char(8), GETDATE(), 112)</c>. The style is emitted as
    ///     written; it is not parameterized.
    /// </summary>
    public static ConvertExpr Convert(SqlType type, Expr value, int style)
        => new(TypeName(type), value, style);

    /// <summary>Converts a column with a T-SQL style code.</summary>
    public static ConvertExpr Convert(SqlType type, IColumn column, int style)
        => Convert(type, column.ToRef(), style);

    // default(SqlType) has no name; emitting CONVERT(, x) would only surface as
    // a syntax error from the server.
    private static string TypeName(SqlType type) => type.Name
        ?? throw new ArgumentException("default(SqlType) is not a SQL type.", nameof(type));
}
