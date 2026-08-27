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

    public bool IsRequired { get; private set; }

    public bool IsUnique { get; private set; }

    public bool HasDefault { get; private set; }

    public object? DefaultValue { get; private set; }

    public IColumn? ReferencedColumn { get; private set; }

    public int? Length { get; private set; }

    internal Type? StorageClrType { get; private set; }

    internal Func<object?, object?>? WriteConverter { get; private set; }

    internal void SetConverter(Type storageClrType, Func<object?, object?> write)
    {
        StorageClrType = storageClrType;
        WriteConverter = write;
    }

    internal void CopyMetadataFrom(IColumn source)
    {
        IsVersion = source.IsVersion;
        IsPrimaryKey = source.IsPrimaryKey;
        IsRequired = source.IsRequired;
        IsUnique = source.IsUnique;
        HasDefault = source.HasDefault;
        DefaultValue = source.DefaultValue;
        ReferencedColumn = source.ReferencedColumn;
        Length = source.Length;
    }

    public ValueExpr Bind(object? value)
    {
        if (WriteConverter is null)
        {
            return new ValueExpr(value, ClrType);
        }

        return new ValueExpr(value is null ? null : WriteConverter(value), StorageClrType!);
    }

    protected void MarkVersion() => IsVersion = true;

    protected void MarkPrimaryKey()
    {
        IsPrimaryKey = true;
        IsRequired = true;
    }

    protected void MarkNotNull() => IsRequired = true;

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

    public BinaryExpr Eq(T value) => Binary(BinaryOp.Eq, value);

    public BinaryExpr Eq(Column<T> other) => Binary(BinaryOp.Eq, other);

    public BinaryExpr Ne(T value) => Binary(BinaryOp.Ne, value);

    public BinaryExpr Ne(Column<T> other) => Binary(BinaryOp.Ne, other);

    public BinaryExpr Gt(T value) => Binary(BinaryOp.Gt, value);

    public BinaryExpr Gt(Column<T> other) => Binary(BinaryOp.Gt, other);

    public BinaryExpr Gte(T value) => Binary(BinaryOp.Gte, value);

    public BinaryExpr Gte(Column<T> other) => Binary(BinaryOp.Gte, other);

    public BinaryExpr Lt(T value) => Binary(BinaryOp.Lt, value);

    public BinaryExpr Lt(Column<T> other) => Binary(BinaryOp.Lt, other);

    public BinaryExpr Lte(T value) => Binary(BinaryOp.Lte, value);

    public BinaryExpr Lte(Column<T> other) => Binary(BinaryOp.Lte, other);

    public UnaryExpr IsNull() => new(UnaryOp.IsNull, ToRef());

    public UnaryExpr IsNotNull() => new(UnaryOp.IsNotNull, ToRef());

    public InExpr In(params T[] values)
        => new(ToRef(), [..values.Select(v => (Expr)Bind(v))]);

    public BetweenExpr Between(T lo, T hi)
        => new(ToRef(), Bind(lo), Bind(hi));

    private BinaryExpr Binary(BinaryOp op, T value)
        => new(op, ToRef(), Bind(value));

    private BinaryExpr Binary(BinaryOp op, Column<T> other)
        => new(op, ToRef(), other.ToRef());

    void IBindableColumn.Bind(string tableAlias) => TableAlias = tableAlias;
}
