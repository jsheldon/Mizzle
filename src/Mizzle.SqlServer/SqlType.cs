namespace Mizzle.SqlServer;

/// <summary>
///     A T-SQL type name for <c>TSql.Convert</c>, e.g. <c>int</c> or
///     <c>varchar(20)</c>. The text is emitted as written; it is not parameterized.
/// </summary>
public readonly struct SqlType : IEquatable<SqlType>
{
    private readonly string? _name;

    private SqlType(string name) => _name = name;

    /// <summary>
    ///     The T-SQL type name. Null on <c>default(SqlType)</c>, which is not a
    ///     type; <c>TSql.Convert</c> rejects it rather than emitting
    ///     <c>CONVERT(, ...)</c>.
    /// </summary>
    public string? Name => _name;

    public static SqlType Int { get; } = new("int");

    public static SqlType BigInt { get; } = new("bigint");

    public static SqlType SmallInt { get; } = new("smallint");

    public static SqlType TinyInt { get; } = new("tinyint");

    public static SqlType Bit { get; } = new("bit");

    public static SqlType Decimal { get; } = new("decimal");

    public static SqlType Numeric { get; } = new("numeric");

    public static SqlType Real { get; } = new("real");

    public static SqlType Float { get; } = new("float");

    public static SqlType DateTime { get; } = new("datetime");

    public static SqlType DateTime2 { get; } = new("datetime2");

    public static SqlType Date { get; } = new("date");

    public static SqlType Timestamp { get; } = new("timestamp");

    public static SqlType UniqueIdentifier { get; } = new("uniqueidentifier");

    public static SqlType Text { get; } = new("text");

    public static SqlType NText { get; } = new("ntext");

    public static SqlType VarCharMax { get; } = new("varchar(max)");

    public static SqlType NVarCharMax { get; } = new("nvarchar(max)");

    public static SqlType Char(int length) => new($"char({Length(length)})");

    public static SqlType VarChar(int length) => new($"varchar({Length(length)})");

    public static SqlType NVarChar(int length) => new($"nvarchar({Length(length)})");

    public bool Equals(SqlType other) => _name == other._name;

    public override bool Equals(object? obj) => obj is SqlType other && Equals(other);

    public override int GetHashCode() => _name?.GetHashCode() ?? 0;

    public override string ToString() => _name ?? "";

    // The name is emitted into SQL verbatim, so a nonsense length must not reach
    // the emitter as varchar(-5).
    private static int Length(int length) => length > 0
        ? length
        : throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be positive.");

}
