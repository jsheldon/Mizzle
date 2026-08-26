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

    public bool IsPrimaryKey { get; private set; }

    public bool IsNotNull { get; private set; }

    public bool IsUnique { get; private set; }

    public bool HasDefault { get; private set; }

    public object? DefaultValue { get; private set; }

    public IColumn? ReferencedColumn { get; private set; }

    public int? Length { get; private set; }

    protected void MarkVersion() => IsVersion = true;

    protected void MarkPrimaryKey() => IsPrimaryKey = true;

    protected void MarkNotNull() => IsNotNull = true;

    protected void MarkUnique() => IsUnique = true;

    protected void MarkDefault(object? value)
    {
        HasDefault = true;
        DefaultValue = value;
    }

    protected void MarkReferences(IColumn column) => ReferencedColumn = column;

    protected void SetLength(int length) => Length = length;

    public ColumnRef ToRef()
        => new(TableAlias ?? Name, Name, typeof(T));

    void IBindableColumn.Bind(string tableAlias) => TableAlias = tableAlias;
}
