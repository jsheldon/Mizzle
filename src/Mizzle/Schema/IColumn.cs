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
    bool IsRequired { get; }
    bool IsUnique { get; }
    bool HasDefault { get; }
    object? DefaultValue { get; }
    IColumn? ReferencedColumn { get; }
    int? Length { get; }
    ColumnRef ToRef();

    // Wraps a domain value for binding, applying the column's storage
    // converter (Map) when present. Nulls pass through unconverted.
    ValueExpr Bind(object? value);
}

internal interface IBindableColumn
{
    void Bind(string tableAlias);
}
