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

    // Excludes this column from MizzleTrimStrings, for values where trailing
    // whitespace is meaningful.
    public PgColumn<T> Untrimmed()
    {
        MarkUntrimmed();
        return this;
    }

    // Binds this column to a differently-named projection member. Returns a
    // copy; the table's own instance is shared across queries and must not change.
    public PgColumn<T> As(string name)
    {
        var column = new PgColumn<T>(Name);
        column.CopyFrom(this);
        column.SetProjectionName(name);
        return column;
    }

    // Converts a legacy storage representation to a domain type. Both
    // arguments must be static method references so the source generators
    // can bake the read conversion into generated mappers.
    public PgColumn<TResult> Map<TResult>(Func<T, TResult> read, Func<TResult, T> write)
    {
        var column = new PgColumn<TResult>(Name);
        column.CopyMetadataFrom(this);
        column.SetConverter(typeof(T), value => read((T)value!), value => write((TResult)value!));
        return column;
    }
}
