namespace Mizzle.Cli.Schema;

internal sealed record ColumnInfo(
    string Schema,
    string Table,
    string Name,
    string StoreType,
    string? NativeType,
    int? Length,
    bool IsNullable,
    bool IsPrimaryKey,
    bool IsIdentity);
