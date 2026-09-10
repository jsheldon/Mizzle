using Mizzle.Ir;
using Mizzle.Schema;

namespace Mizzle.Fluent;

public static class ColumnOperators
{
    public static BinaryExpr Like(this Column<string> column, string pattern)
        => new(BinaryOp.Like, column.ToRef(), new ValueExpr(pattern, typeof(string)));

    public static BinaryExpr ILike(this Column<string> column, string pattern)
        => new(BinaryOp.ILike, column.ToRef(), new ValueExpr(pattern, typeof(string)));

    /// <summary>
    ///     LIKE with an explicit ESCAPE character, so % and _ in <paramref name="pattern"/> can be
    ///     matched literally. Build an escaped pattern with <see cref="LikePattern"/> rather than
    ///     hand-escaping search text.
    /// </summary>
    public static LikeExpr Like(this Column<string> column, string pattern, char escape)
        => new(column.ToRef(), new ValueExpr(pattern, typeof(string)), escape, CaseInsensitive: false);

    /// <summary>Case-insensitive counterpart of <see cref="Like(Column{string},string,char)"/>.</summary>
    public static LikeExpr ILike(this Column<string> column, string pattern, char escape)
        => new(column.ToRef(), new ValueExpr(pattern, typeof(string)), escape, CaseInsensitive: true);
}
