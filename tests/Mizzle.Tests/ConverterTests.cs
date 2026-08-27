namespace Mizzle.Tests;

file static class EhrConvert
{
    public static Guid ToGuid(string value) => Guid.Parse(value);

    public static string FromGuid(Guid value) => value.ToString("D");

    public static DateOnly ToDate(string value) => DateOnly.ParseExact(value, "yyyyMMdd");

    public static string FromDate(DateOnly value) => value.ToString("yyyyMMdd");
}

file sealed class LegacyPersons : SqlTable<LegacyPersons>
{
    public LegacyPersons() : base("person", "dbo", "a") { }

    public SqlColumn<Guid> PersonId { get; } = Char("person_id", 36).Map(EhrConvert.ToGuid, EhrConvert.FromGuid).PrimaryKey();
    public SqlColumn<DateOnly> DateOfBirth { get; } = Char("date_of_birth", 8).NotNull().Map(EhrConvert.ToDate, EhrConvert.FromDate);
    public SqlColumn<string> FirstName { get; } = VarChar("first_name", 50).NotNull();
    public SqlColumn<DateTime> Created { get; } = DateTime("create_timestamp");
    public SqlColumn<DateOnly> Reviewed { get; } = Date("review_date");
    public SqlColumn<byte[]> RowVer { get; } = Timestamp("row_ver");
}

public sealed class ConverterTests
{
    private static (string Sql, IReadOnlyList<object?> Values) EmitSqlServer(SelectQuery q)
    {
        var (canonical, values) = Parameterizer.Run(q);
        return (new SqlServerEmitter().Emit(canonical, values).Sql, values);
    }

    [Fact]
    public void New_factories_carry_expected_clr_types_and_lengths()
    {
        var t = new LegacyPersons();
        Assert.Equal(typeof(Guid), t.PersonId.ClrType);
        Assert.Equal(typeof(DateOnly), t.DateOfBirth.ClrType);
        Assert.Equal(50, t.FirstName.Length);
        Assert.Equal(typeof(DateTime), t.Created.ClrType);
        Assert.Equal(typeof(DateOnly), t.Reviewed.ClrType);
        Assert.Equal(typeof(byte[]), t.RowVer.ClrType);
    }

    [Fact]
    public void Map_preserves_name_and_metadata_on_either_side_of_modifiers()
    {
        var t = new LegacyPersons();
        // Modifier after Map
        Assert.Equal("person_id", t.PersonId.Name);
        Assert.True(t.PersonId.IsPrimaryKey);
        Assert.True(t.PersonId.IsRequired);
        // Modifier before Map (copied through)
        Assert.True(t.DateOfBirth.IsRequired);
        Assert.Equal("date_of_birth", t.DateOfBirth.Name);
        Assert.Equal("a", t.PersonId.ToRef().TableAlias);
    }

    [Fact]
    public void Typed_operators_bind_converted_storage_values()
    {
        var t = new LegacyPersons();
        var id = Guid.NewGuid();
        var q = new SelectBuilder()
            .Select(t.FirstName)
            .From(t)
            .Where(t.PersonId.Eq(id))
            .Where(t.DateOfBirth.Gte(new DateOnly(2000, 1, 22)))
            .OrderBy(t.FirstName)
            .Build();
        var (sql, values) = EmitSqlServer(q);
        Assert.Contains("[a].[person_id] = @p0", sql, StringComparison.Ordinal);
        Assert.Equal([id.ToString("D"), "20000122"], values);
    }

    [Fact]
    public void Loose_where_and_write_builders_bind_converted_values()
    {
        var t = new LegacyPersons();
        var id = Guid.NewGuid();

        var select = new SelectBuilder().Select(t.FirstName).From(t).Where(t.PersonId, id).Build();
        Assert.Equal([id.ToString("D")], Parameterizer.Run(select).Values);

        var update = new UpdateBuilder(t)
            .Set(t.DateOfBirth, new DateOnly(2010, 1, 22))
            .Where(t.PersonId, id)
            .Build();
        Assert.Equal(["20100122", id.ToString("D")], Parameterizer.Run(update).Values);

        var insert = new InsertBuilder(t).Value(t.PersonId, id).Build();
        Assert.Equal([id.ToString("D")], Parameterizer.Run(insert).Values);

        var delete = new DeleteBuilder(t).Where(t.PersonId, id).Build();
        Assert.Equal([id.ToString("D")], Parameterizer.Run(delete).Values);
    }

    [Fact]
    public void In_and_between_bind_converted_values()
    {
        var t = new LegacyPersons();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var q = new SelectBuilder().Select(t.FirstName).From(t)
            .Where(t.PersonId.In(a, b))
            .Where(t.DateOfBirth.Between(new DateOnly(2000, 1, 1), new DateOnly(2001, 1, 1)))
            .Build();
        Assert.Equal(
            [a.ToString("D"), b.ToString("D"), "20000101", "20010101"],
            Parameterizer.Run(q).Values);
    }

    [Fact]
    public void Null_values_bind_as_null_without_converting()
    {
        var t = new LegacyPersons();
        var q = new SelectBuilder().Select(t.FirstName).From(t).Where(t.PersonId, null).Build();
        Assert.Equal([null], Parameterizer.Run(q).Values);
    }
}
