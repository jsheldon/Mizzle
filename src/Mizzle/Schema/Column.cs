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

    protected void MarkVersion() => IsVersion = true;

    protected void MarkPrimaryKey() => IsPrimaryKey = true;

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
        => new(ToRef(), [..values.Select(v => (Expr)new ValueExpr(v, typeof(T)))]);

    public BetweenExpr Between(T lo, T hi)
        => new(ToRef(), new ValueExpr(lo, typeof(T)), new ValueExpr(hi, typeof(T)));

    private BinaryExpr Binary(BinaryOp op, T value)
        => new(op, ToRef(), new ValueExpr(value, typeof(T)));

    private BinaryExpr Binary(BinaryOp op, Column<T> other)
        => new(op, ToRef(), other.ToRef());

    void IBindableColumn.Bind(string tableAlias) => TableAlias = tableAlias;
}
