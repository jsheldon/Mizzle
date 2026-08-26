using Mizzle.Ir;

namespace Mizzle.SqlServer;

public static class TSql
{
    public static CallExpr Len(Expr value) => new("len", [value], DialectKind.SqlServer);

    public static CallExpr GetUtcDate() => new("getutcdate", [], DialectKind.SqlServer);
}
