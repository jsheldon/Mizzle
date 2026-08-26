using Mizzle.Schema;

namespace Mizzle.Postgres;

public abstract class PgTable<TSelf> : Table<TSelf>
    where TSelf : PgTable<TSelf>
{
    protected PgTable(string name, string? schema = null, string? alias = null)
        : base(name, schema, alias)
    {
    }

    public override DialectKind Dialect => DialectKind.Postgres;

    protected static PgColumn<string> Text(string name) => new(name);

    protected static PgColumn<int> Integer(string name) => new(name);

    protected static PgColumn<int> Identity(string name) => new(name);
}
