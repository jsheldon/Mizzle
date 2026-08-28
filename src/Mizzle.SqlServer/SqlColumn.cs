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

    // Excludes this column from MizzleTrimStrings, for values where trailing
    // whitespace is meaningful.
    public SqlColumn<T> Untrimmed()
    {
        MarkUntrimmed();
        return this;
    }

    // Binds this column to a differently-named projection member. Returns a
    // copy; the table's own instance is shared across queries and must not change.
    public SqlColumn<T> As(string name)
    {
        var column = new SqlColumn<T>(Name);
        column.CopyFrom(this);
        column.SetProjectionName(name);
        return column;
    }

    // Converts a legacy storage representation to a domain type. Both
    // arguments must be static method references so the source generators
    // can bake the read conversion into generated mappers.
    public SqlColumn<TResult> Map<TResult>(Func<T, TResult> read, Func<TResult, T> write)
    {
        var column = new SqlColumn<TResult>(Name);
        column.CopyMetadataFrom(this);
        column.SetConverter(typeof(T), value => read((T)value!), value => write((TResult)value!));
        return column;
    }
}
