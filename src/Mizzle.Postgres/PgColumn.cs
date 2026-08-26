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
}
