using Mizzle.Ir;

namespace Mizzle.Schema;

public interface IColumn
{
    string Name { get; }
    Type ClrType { get; }
    DialectKind Dialect { get; }
    string? TableAlias { get; }
    bool IsVersion { get; }
    bool IsPrimaryKey { get; }
    bool IsNotNull { get; }
    bool IsUnique { get; }
    bool HasDefault { get; }
    object? DefaultValue { get; }
    IColumn? ReferencedColumn { get; }
    int? Length { get; }
    ColumnRef ToRef();
}

internal interface IBindableColumn
{
    void Bind(string tableAlias);
}
