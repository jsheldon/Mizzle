using Mizzle.Ir;

namespace Mizzle.Schema;

public interface IColumn
{
    string Name { get; }
    Type ClrType { get; }
    DialectKind Dialect { get; }
    string? TableAlias { get; }
    bool IsVersion { get; }
    ColumnRef ToRef();
}

internal interface IBindableColumn
{
    void Bind(string tableAlias);
}
