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
}
