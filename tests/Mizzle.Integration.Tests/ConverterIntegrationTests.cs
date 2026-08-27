namespace Mizzle.Integration.Tests;

public static class LegacyConvert
{
    public static Guid ToGuid(string value) => Guid.Parse(value);

    public static string FromGuid(Guid value) => value.ToString("D");

    public static DateOnly ToDate(string value) => DateOnly.ParseExact(value, "yyyyMMdd");

    public static string FromDate(DateOnly value) => value.ToString("yyyyMMdd");
}

file sealed class LegacyPersons : PgTable<LegacyPersons>
{
    public LegacyPersons() : base("legacy_person", "public", "a") { }

    public PgColumn<Guid> PersonId { get; } = Char("person_id", 36).Map(LegacyConvert.ToGuid, LegacyConvert.FromGuid).PrimaryKey();
    public PgColumn<DateOnly> DateOfBirth { get; } = Char("date_of_birth", 8).NotNull().Map(LegacyConvert.ToDate, LegacyConvert.FromDate);
    public PgColumn<string> FirstName { get; } = Text("first_name").NotNull();
}

public sealed class ConverterIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fx;

    public ConverterIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Converted_columns_round_trip_through_generated_projection()
    {
        var id = Guid.NewGuid();
        await using (var conn = await _fx.DataSource.OpenConnectionAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS public.legacy_person (
                  person_id char(36) PRIMARY KEY,
                  date_of_birth char(8) NOT NULL,
                  first_name text NOT NULL
                );
                DELETE FROM public.legacy_person;
                INSERT INTO public.legacy_person (person_id, date_of_birth, first_name)
                  VALUES ('{id:D}', '20100122', 'Ada');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var db = new PostgresDb(_fx.DataSource);
        var a = new LegacyPersons();

        // Bind side: Where by Guid sends a string parameter (char = char).
        var row = await db.Select(a.PersonId, a.DateOfBirth, a.FirstName)
            .From(a)
            .Where(a.PersonId.Eq(id))
            .FirstOrDefaultAsync<LegacyProfile>();

        Assert.NotNull(row);
        Assert.Equal(id, row!.PersonId);
        Assert.Equal(new DateOnly(2010, 1, 22), row.DateOfBirth);
        Assert.Equal("Ada", row.FirstName);
    }
}
