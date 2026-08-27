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

    // Converts a legacy storage representation to a domain type. Both
    // arguments must be static method references so the source generators
    // can bake the read conversion into generated mappers.
    public SqlColumn<TResult> Map<TResult>(Func<T, TResult> read, Func<TResult, T> write)
    {
        _ = read; // generator-facing; runtime reads use generated or hand-written mappers
        var column = new SqlColumn<TResult>(Name);
        column.CopyMetadataFrom(this);
        column.SetConverter(typeof(T), value => write((TResult)value!));
        return column;
    }
}
