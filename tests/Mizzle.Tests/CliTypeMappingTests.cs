using Mizzle.Cli.Infrastructure;
using Mizzle.Cli.Schema;

namespace Mizzle.Tests;

public sealed class CliTypeMappingTests
{
    [Fact]
    public void Postgres_identity_maps_to_identity_factory()
    {
        var column = new ColumnInfo("public", "users", "id", "integer", "int4", null, false, true, true);

        var mapping = TypeMappings.Resolve(ProviderKind.Postgres, column);

        Assert.Equal("int", mapping.ClrType);
        Assert.Equal("Identity", mapping.Factory);
    }

    [Fact]
    public void Sql_server_nvarchar_requires_length()
    {
        var column = new ColumnInfo("dbo", "users", "email", "nvarchar", "nvarchar", 255, false, false, false);

        var mapping = TypeMappings.Resolve(ProviderKind.SqlServer, column);

        Assert.Equal("string", mapping.ClrType);
        Assert.Equal("NVarChar", mapping.Factory);
        Assert.True(mapping.NeedsLength);
    }

    [Theory]
    [InlineData("smallint", "short", "SmallInt")]
    [InlineData("decimal", "decimal", "Decimal")]
    [InlineData("numeric", "decimal", "Numeric")]
    [InlineData("real", "float", "Real")]
    [InlineData("text", "string", "Text")]
    [InlineData("float", "double", "Float")]
    [InlineData("tinyint", "byte", "TinyInt")]
    [InlineData("ntext", "string", "NText")]
    public void Sql_server_common_legacy_types_are_supported(string storeType, string clrType, string factory)
    {
        var column = new ColumnInfo("dbo", "legacy", "value", storeType, storeType, null, true, false, false);

        var mapping = TypeMappings.Resolve(ProviderKind.SqlServer, column);

        Assert.Equal(clrType, mapping.ClrType);
        Assert.Equal(factory, mapping.Factory);
        Assert.False(mapping.NeedsLength);
    }

    [Fact]
    public void Unsupported_type_fails_with_clear_code()
    {
        var column = new ColumnInfo("public", "users", "location", "point", "point", null, true, false, false);

        var ex = Assert.Throws<CliFailure>(() => TypeMappings.Resolve(ProviderKind.Postgres, column));

        Assert.Equal("MZCLI021", ex.Code);
        Assert.Contains("public.users.location", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Unsupported Postgres type 'point'", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("smallint", "int2", "short", "SmallInt")]
    [InlineData("numeric", "numeric", "decimal", "Numeric")]
    [InlineData("money", "money", "decimal", "Money")]
    [InlineData("real", "float4", "float", "Real")]
    [InlineData("double precision", "float8", "double", "DoublePrecision")]
    [InlineData("timestamp without time zone", "timestamp", "DateTime", "Timestamp")]
    [InlineData("time without time zone", "time", "TimeOnly", "Time")]
    [InlineData("bytea", "bytea", "byte[]", "Bytea")]
    [InlineData("json", "json", "string", "Json")]
    [InlineData("jsonb", "jsonb", "string", "Jsonb")]
    public void Postgres_expanded_types_are_supported(string storeType, string nativeType, string clrType, string factory)
    {
        var column = new ColumnInfo("public", "readings", "value", storeType, nativeType, null, true, false, false);

        var mapping = TypeMappings.Resolve(ProviderKind.Postgres, column);

        Assert.Equal(clrType, mapping.ClrType);
        Assert.Equal(factory, mapping.Factory);
    }
}
