using Mizzle.Ir;

namespace Mizzle.Schema;

public abstract class Column<T> : IColumn, IBindableColumn
{
    protected Column(string name, DialectKind dialect)
    {
        Name = name;
        Dialect = dialect;
    }

    public string Name { get; }
    public Type ClrType => typeof(T);
    public DialectKind Dialect { get; }
    public string? TableAlias { get; private set; }

    public bool IsVersion { get; private set; }

    protected void MarkVersion() => IsVersion = true;

    public ColumnRef ToRef()
        => new(TableAlias ?? Name, Name, typeof(T));

    void IBindableColumn.Bind(string tableAlias) => TableAlias = tableAlias;
}
