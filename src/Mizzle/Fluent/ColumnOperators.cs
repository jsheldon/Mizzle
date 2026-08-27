using Mizzle.Ir;
using Mizzle.Schema;

namespace Mizzle.Fluent;

public static class ColumnOperators
{
    public static BinaryExpr Like(this Column<string> column, string pattern)
        => new(BinaryOp.Like, column.ToRef(), new ValueExpr(pattern, typeof(string)));

    public static BinaryExpr ILike(this Column<string> column, string pattern)
        => new(BinaryOp.ILike, column.ToRef(), new ValueExpr(pattern, typeof(string)));
}
