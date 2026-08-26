using Mizzle.Schema;

namespace Mizzle.Postgres;

public sealed class PgColumn<T> : Column<T>
{
    internal PgColumn(string name) : base(name, DialectKind.Postgres)
    {
    }

    public PgColumn<T> Version()
    {
        MarkVersion();
        return this;
    }

    public PgColumn<T> PrimaryKey()
    {
        MarkPrimaryKey();
        return this;
    }

    public PgColumn<T> NotNull()
    {
        MarkNotNull();
        return this;
    }

    public PgColumn<T> Unique()
    {
        MarkUnique();
        return this;
    }

    public PgColumn<T> Default(T value)
    {
        MarkDefault(value);
        return this;
    }

    public PgColumn<T> References(IColumn column)
    {
        MarkReferences(column);
        return this;
    }

    internal PgColumn<T> WithLength(int length)
    {
        SetLength(length);
        return this;
    }
}
