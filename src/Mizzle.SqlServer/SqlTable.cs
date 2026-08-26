using Mizzle.Schema;

namespace Mizzle.SqlServer;

public abstract class SqlTable<TSelf> : Table<TSelf>
    where TSelf : SqlTable<TSelf>
{
    protected SqlTable(string name, string? schema = null, string? alias = null)
        : base(name, schema, alias)
    {
    }

    public override DialectKind Dialect => DialectKind.SqlServer;

    protected static SqlColumn<string> NVarChar(string name, int length)
    {
        _ = length;
        return new SqlColumn<string>(name);
    }

    protected static SqlColumn<int> Int(string name) => new(name);

    protected static SqlColumn<int> Identity(string name) => new(name);
}
