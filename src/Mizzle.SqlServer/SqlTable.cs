using Mizzle.Schema;

namespace Mizzle.SqlServer;

public abstract class SqlTable<TSelf> : Table<TSelf>
    where TSelf : SqlTable<TSelf>
{
    protected SqlTable(string name, string? schema = null)
        : base(name, schema)
    {
    }

    public override DialectKind Dialect => DialectKind.SqlServer;

    protected static SqlColumn<string> NVarChar(string name, int length) => new SqlColumn<string>(name).WithLength(length);

    protected static SqlColumn<int> Int(string name) => new(name);

    protected static SqlColumn<int> Identity(string name) => new(name);

    protected static SqlColumn<string> NVarCharMax(string name) => new(name);

    protected static SqlColumn<string> Char(string name, int length) => new SqlColumn<string>(name).WithLength(length);

    protected static SqlColumn<string> VarChar(string name, int length) => new SqlColumn<string>(name).WithLength(length);

    protected static SqlColumn<DateTime> DateTime(string name) => new(name);

    protected static SqlColumn<DateOnly> Date(string name) => new(name);

    protected static SqlColumn<byte[]> Timestamp(string name) => new(name);

    protected static SqlColumn<DateTime> DateTime2(string name) => new(name);

    protected static SqlColumn<bool> Bit(string name) => new(name);

    protected static SqlColumn<long> BigInt(string name) => new(name);

    protected static SqlColumn<Guid> UniqueIdentifier(string name) => new(name);
}
