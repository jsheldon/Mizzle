using Mizzle.Ir;

namespace Mizzle.Postgres;

public static class Pg
{
    public static CallExpr Lower(Expr value) => new("lower", [value], DialectKind.Postgres);

    public static CallExpr Now() => new("now", [], DialectKind.Postgres);
}
