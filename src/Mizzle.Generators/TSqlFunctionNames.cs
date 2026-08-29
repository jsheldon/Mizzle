namespace Mizzle.Generators;

// As with SqlTypeNames, the baker works on syntax and never runs
// Mizzle.SqlServer, so this mirrors the function names TSql builds into CallExpr.
// TSqlFunctionNameParityTests pins the two together. Convert is deliberately
// absent: it is a ConvertExpr, not a call, and ResolveConvertSql handles it.
internal static class TSqlFunctionNames
{
    public static string? For(string member, int arity) => (member, arity) switch
    {
        ("GetDate", 0) => "getdate",
        ("GetUtcDate", 0) => "getutcdate",
        ("Len", 1) => "len",
        ("RTrim", 1) => "rtrim",
        ("LTrim", 1) => "ltrim",
        ("Upper", 1) => "upper",
        ("Lower", 1) => "lower",
        _ => null
    };
}
