namespace Mizzle.Generators;

// The baker reconstructs a SqlType's text from the member that produced it,
// because it works on syntax and never runs Mizzle.SqlServer. That makes this a
// second copy of SqlType.Name, so SqlTypeNameParityTests pins the two together:
// a SqlType member added without a case here silently drops off the baked path.
internal static class SqlTypeNames
{
    public static string? For(string member, int? length) => (member, length) switch
    {
        ("Int", null) => "int",
        ("BigInt", null) => "bigint",
        ("SmallInt", null) => "smallint",
        ("TinyInt", null) => "tinyint",
        ("Bit", null) => "bit",
        ("Decimal", null) => "decimal",
        ("Numeric", null) => "numeric",
        ("Real", null) => "real",
        ("Float", null) => "float",
        ("DateTime", null) => "datetime",
        ("DateTime2", null) => "datetime2",
        ("Date", null) => "date",
        ("Timestamp", null) => "timestamp",
        ("UniqueIdentifier", null) => "uniqueidentifier",
        ("Text", null) => "text",
        ("NText", null) => "ntext",
        ("VarCharMax", null) => "varchar(max)",
        ("NVarCharMax", null) => "nvarchar(max)",
        ("Char", int n) when n > 0 => "char(" + n + ")",
        ("VarChar", int n) when n > 0 => "varchar(" + n + ")",
        ("NVarChar", int n) when n > 0 => "nvarchar(" + n + ")",
        _ => null
    };
}
