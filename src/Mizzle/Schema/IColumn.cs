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
    bool HasDefault { get; }
    object? DefaultValue { get; }
    IColumn? ReferencedColumn { get; }
    int? Length { get; }

    // Projection alias set by As(...) at the select site. Null when unaliased.
    string? ProjectionName { get; }

    // Opts this column out of MizzleTrimStrings.
    bool IsUntrimmed { get; }

    // Queries against this column's table must mention it in WHERE (MIZ013).
    bool IsAlwaysFilter { get; }
    ColumnRef ToRef();

    // Wraps a domain value for binding, applying the column's storage
    // converter (Map) when present. Nulls pass through unconverted.
    ValueExpr Bind(object? value);
}

internal interface IBindableColumn
{
    void Bind(string tableAlias);
}

internal interface IRuntimeReadableColumn
{
    object? ReadValue(System.Data.Common.DbDataReader reader, int ordinal);
}
