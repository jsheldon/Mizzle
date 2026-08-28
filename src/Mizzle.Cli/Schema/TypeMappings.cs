using Mizzle.Cli.Infrastructure;

namespace Mizzle.Cli.Schema;

internal sealed record TypeMapping(string StoreType, string ClrType, string Factory, bool NeedsLength = false);

internal static class TypeMappings
{
    public static IReadOnlyList<TypeMapping> For(ProviderKind provider)
        => provider switch
        {
            ProviderKind.Postgres =>
            [
                new("integer", "int", "Integer"),
                new("int4", "int", "Integer"),
                new("bigint", "long", "BigInt"),
                new("int8", "long", "BigInt"),
                new("text", "string", "Text"),
                new("character varying", "string", "Varchar", true),
                new("varchar", "string", "Varchar", true),
                new("character", "string", "Char", true),
                new("bpchar", "string", "Char", true),
                new("boolean", "bool", "Boolean"),
                new("bool", "bool", "Boolean"),
                new("uuid", "Guid", "Uuid"),
                new("date", "DateOnly", "Date"),
                new("timestamp with time zone", "DateTimeOffset", "Timestamptz"),
                new("timestamptz", "DateTimeOffset", "Timestamptz"),
            ],
            ProviderKind.SqlServer =>
            [
                new("int", "int", "Int"),
                new("smallint", "short", "SmallInt"),
                new("tinyint", "byte", "TinyInt"),
                new("bigint", "long", "BigInt"),
                new("decimal", "decimal", "Decimal"),
                new("numeric", "decimal", "Numeric"),
                new("real", "float", "Real"),
                new("float", "double", "Float"),
                new("nvarchar", "string", "NVarChar", true),
                new("varchar", "string", "VarChar", true),
                new("nchar", "string", "NVarChar", true),
                new("char", "string", "Char", true),
                new("text", "string", "Text"),
                new("ntext", "string", "NText"),
                new("bit", "bool", "Bit"),
                new("uniqueidentifier", "Guid", "UniqueIdentifier"),
                new("date", "DateOnly", "Date"),
                new("datetime", "DateTime", "DateTime"),
                new("datetime2", "DateTime", "DateTime2"),
                new("timestamp", "byte[]", "Timestamp"),
                new("rowversion", "byte[]", "Timestamp"),
            ],
            _ => throw new CliFailure("MZCLI002", $"Provider '{provider}' is not implemented.")
        };

    public static TypeMapping Resolve(ProviderKind provider, ColumnInfo column)
    {
        if (provider == ProviderKind.Postgres && column.IsIdentity && (column.StoreType == "integer" || column.NativeType == "int4"))
        {
            return new TypeMapping(column.StoreType, "int", "Identity");
        }

        if (provider == ProviderKind.SqlServer && column.IsIdentity && column.StoreType == "int")
        {
            return new TypeMapping(column.StoreType, "int", "Identity");
        }

        var key = provider == ProviderKind.Postgres ? column.NativeType ?? column.StoreType : column.StoreType;
        var mapping = For(provider).FirstOrDefault(m => string.Equals(m.StoreType, key, StringComparison.OrdinalIgnoreCase))
            ?? For(provider).FirstOrDefault(m => string.Equals(m.StoreType, column.StoreType, StringComparison.OrdinalIgnoreCase));
        return mapping ?? throw new CliFailure(
            "MZCLI021",
            $"Unsupported {provider} type '{key}' on {column.Schema}.{column.Table}.{column.Name}.",
            "Add a Mizzle column factory/type mapping, exclude the column, or scaffold this table by hand.");
    }
}
