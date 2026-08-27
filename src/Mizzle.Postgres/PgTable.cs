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

    protected static PgColumn<string> Varchar(string name, int length) => new PgColumn<string>(name).WithLength(length);

    protected static PgColumn<DateTimeOffset> Timestamptz(string name) => new(name);

    protected static PgColumn<bool> Boolean(string name) => new(name);

    protected static PgColumn<long> BigInt(string name) => new(name);

    protected static PgColumn<Guid> Uuid(string name) => new(name);

    protected static PgColumn<string> Char(string name, int length) => new PgColumn<string>(name).WithLength(length);

    protected static PgColumn<DateOnly> Date(string name) => new(name);
}
