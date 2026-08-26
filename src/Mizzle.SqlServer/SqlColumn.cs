using Mizzle.Schema;

namespace Mizzle.SqlServer;

public sealed class SqlColumn<T> : Column<T>
{
    internal SqlColumn(string name) : base(name, DialectKind.SqlServer)
    {
    }

    public SqlColumn<T> Version()
    {
        MarkVersion();
        return this;
    }

    public SqlColumn<T> PrimaryKey()
    {
        MarkPrimaryKey();
        return this;
    }

    public SqlColumn<T> NotNull()
    {
        MarkNotNull();
        return this;
    }

    public SqlColumn<T> Unique()
    {
        MarkUnique();
        return this;
    }

    public SqlColumn<T> Default(T value)
    {
        MarkDefault(value);
        return this;
    }

    public SqlColumn<T> References(IColumn column)
    {
        MarkReferences(column);
        return this;
    }

    internal SqlColumn<T> WithLength(int length)
    {
        SetLength(length);
        return this;
    }
}
