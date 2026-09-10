using Mizzle.Fluent;
using Mizzle.SqlServer;

namespace Mizzle.Integration.Tests;

// Acceptance scenario for the three After/Like/dynamic-mapper pieces landing together: a
// faceted patient search with a fixed result shape and a variable set of optional predicates,
// composed with ordinary `if` + reassignment rather than a combinatorial WhereIf-baked chain.
// Exercises representative filter combinations, the grouped phone OR, preserved left-join
// nullability, one mapper generated regardless of which filters ran, and cursor boundaries
// including a null sort key.
// Namespace-level and public: the generator's dynamic mapper for the delegate-free
// ToCursorPageAsync<T>() terminator below emits into Mizzle.Generated.Projections, a
// different top-level class, so T must be reachable from there -- a private nested
// record would not be.
public sealed record PatientSearchRow(
    Guid PersonId, string FirstName, string LastName, string? MiddleName,
    string? MedRecNbr, DateOnly? DateOfBirth, string? ExclInd);

public sealed record PatientSearchCursor(string LastName, string FirstName, DateOnly? DateOfBirth, Guid PersonId);

public sealed class PatientSearchAcceptanceTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fx;

    public PatientSearchAcceptanceTests(SqlServerFixture fx) => _fx = fx;

    private static async Task<IReadOnlyList<PatientSearchRow>> SearchAsync(
        SqlDb db,
        string practiceId,
        string enterpriseId,
        int userId,
        string? firstNamePattern = null,
        string? lastNamePattern = null,
        string? middleNamePattern = null,
        string? personNumber = null,
        string? medicalRecordNumber = null,
        DateOnly? dateOfBirth = null,
        string? phonePattern = null,
        string? emailPattern = null,
        PatientSearchCursor? cursor = null,
        int pageSize = 50)
    {
        var a = new Person().WithAlias("a");
        var b = new Patient().WithAlias("b");
        var c = new UserPersonFilter().WithAlias("c");

        var q = db.Select(
                a.PersonId, a.FirstName, a.LastName, a.MiddleName,
                b.MedRecNbr, a.DateOfBirth, c.ExclInd)
            .From(a)
            .InnerJoin(b).On(a.PersonId.Eq(b.PersonId))
            .LeftJoin(c).On(a.PersonId.Eq(c.PersonId), c.UserId.Eq(userId))
            .Where(
                a.PracticeId.Eq(practiceId),
                a.EnterpriseId.Eq(enterpriseId),
                Sql.Or(c.InclInd.Eq("Y"), c.InclInd.IsNull()),
                Sql.Or(c.ExclInd.IsNull(), c.ExclInd.Ne("Y")));

        if (firstNamePattern is { } first)
        {
            q = q.Where(a.FirstName.Like(LikePattern.Contains(first, '\\'), '\\'));
        }

        if (lastNamePattern is { } last)
        {
            q = q.Where(a.LastName.Like(LikePattern.Contains(last, '\\'), '\\'));
        }

        if (middleNamePattern is { } middle)
        {
            q = q.Where(a.MiddleName.Like(LikePattern.Contains(middle, '\\'), '\\'));
        }

        if (personNumber is { } nbr)
        {
            q = q.Where(a.PersonNbr.Eq(nbr));
        }

        if (medicalRecordNumber is { } mrn)
        {
            q = q.Where(b.MedRecNbr.Eq(mrn));
        }

        if (dateOfBirth is { } dob)
        {
            q = q.Where(a.DateOfBirth.Eq(dob));
        }

        if (phonePattern is { } phone)
        {
            var pattern = LikePattern.Contains(phone, '\\');
            q = q.Where(Sql.Or(
                a.DayPhone.Like(pattern, '\\'),
                a.HomePhone.Like(pattern, '\\'),
                a.CellPhone.Like(pattern, '\\'),
                a.AltPhone.Like(pattern, '\\')));
        }

        if (emailPattern is { } email)
        {
            q = q.Where(a.EmailAddress.Like(LikePattern.Contains(email, '\\'), '\\'));
        }

        q = q.OrderBy(a.LastName)
            .OrderBy(a.FirstName)
            .OrderBy(a.DateOfBirth)
            .OrderBy(a.PersonId);

        if (cursor is { } after)
        {
            q = q.After(
                (a.LastName, after.LastName),
                (a.FirstName, after.FirstName),
                (a.DateOfBirth, after.DateOfBirth),
                (a.PersonId, after.PersonId));
        }

        // Delegate-free: the query above is composed entirely through ordinary `if` +
        // reassignment (not WhereIf), so this relies on the generator tracing every
        // reassignment as projection-preserving and forwarding to the delegate-based
        // ToCursorPageAsync overload with a generated mapper -- today's fix, not a
        // hand-written row mapper standing in for it.
        var page = await q.Limit(pageSize).ToCursorPageAsync<PatientSearchRow>();

        return page.Items;
    }

    private async Task SeedAsync()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            IF OBJECT_ID(N'dbo.person', N'U') IS NOT NULL DROP TABLE dbo.person;
            IF OBJECT_ID(N'dbo.patient', N'U') IS NOT NULL DROP TABLE dbo.patient;
            IF OBJECT_ID(N'dbo.user_person_filter', N'U') IS NOT NULL DROP TABLE dbo.user_person_filter;

            CREATE TABLE dbo.person (
                person_id uniqueidentifier NOT NULL PRIMARY KEY,
                person_nbr varchar(20) NOT NULL,
                first_name varchar(60) NOT NULL,
                last_name varchar(60) NOT NULL,
                middle_name varchar(25) NULL,
                date_of_birth date NULL,
                day_phone varchar(10) NULL,
                home_phone varchar(10) NULL,
                cell_phone varchar(10) NULL,
                alt_phone varchar(10) NULL,
                email_address varchar(80) NULL,
                practice_id char(4) NOT NULL,
                enterprise_id char(5) NOT NULL
            );
            CREATE TABLE dbo.patient (
                person_id uniqueidentifier NOT NULL PRIMARY KEY,
                med_rec_nbr varchar(20) NULL
            );
            CREATE TABLE dbo.user_person_filter (
                person_id uniqueidentifier NOT NULL,
                user_id int NOT NULL,
                incl_ind char(1) NULL,
                excl_ind char(1) NULL
            );

            INSERT INTO dbo.person (person_id, person_nbr, first_name, last_name, middle_name, date_of_birth, day_phone, home_phone, cell_phone, alt_phone, email_address, practice_id, enterprise_id)
            VALUES
                ('11111111-1111-1111-1111-111111111111', 'P001', 'Jordan', 'Smith', NULL, '1990-01-01', NULL, NULL, '5551111111', NULL, 'jordan@example.com', '0001', '00001'),
                ('22222222-2222-2222-2222-222222222222', 'P002', 'Alex', 'Smith', NULL, NULL, NULL, NULL, NULL, NULL, 'alex@example.com', '0001', '00001'),
                ('33333333-3333-3333-3333-333333333333', 'P003', 'Jamie', 'Doe', NULL, '1985-05-05', '5552222222', NULL, NULL, NULL, 'jamie@example.com', '0001', '00001'),
                ('44444444-4444-4444-4444-444444444444', 'P004', 'Taylor', 'Jones', NULL, '1990-01-01', NULL, NULL, NULL, NULL, 'taylor@example.com', '9999', '00001'),
                ('55555555-5555-5555-5555-555555555555', 'P005', 'Pat', 'Smith', 'Q', '1990-01-01', NULL, NULL, NULL, NULL, 'pat@example.com', '0001', '00001'),
                ('66666666-6666-6666-6666-666666666666', 'P006', 'Morgan', 'Smith', NULL, '1990-01-01', NULL, '5553333333', NULL, NULL, 'morgan@example.com', '0001', '00001');

            INSERT INTO dbo.patient (person_id, med_rec_nbr)
            VALUES
                ('11111111-1111-1111-1111-111111111111', 'MRN1'),
                ('22222222-2222-2222-2222-222222222222', 'MRN2'),
                ('33333333-3333-3333-3333-333333333333', 'MRN3'),
                ('44444444-4444-4444-4444-444444444444', 'MRN4'),
                ('55555555-5555-5555-5555-555555555555', 'MRN5'),
                ('66666666-6666-6666-6666-666666666666', 'MRN6');

            INSERT INTO dbo.user_person_filter (person_id, user_id, incl_ind, excl_ind)
            VALUES
                ('22222222-2222-2222-2222-222222222222', 1, 'Y', NULL),
                ('66666666-6666-6666-6666-666666666666', 1, NULL, 'Y');
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    [DockerFact]
    public async Task LastName_filter_excludes_non_matching_and_excluded_rows()
    {
        await SeedAsync();
        var db = new SqlDb(_fx.DataSource);

        var results = await SearchAsync(db, "0001", "00001", userId: 1, lastNamePattern: "Smith");

        // Jamie Doe never matches (last name). Morgan Smith matches the last name but is
        // excluded (excl_ind='Y') and must not appear despite matching. Taylor Jones is a
        // different practice. Ordered by (LastName, FirstName): all four remaining share
        // "Smith", so FirstName decides -- Alex, Jordan, Pat alphabetically.
        Assert.Equal(["Alex", "Jordan", "Pat"], results.Select(r => r.FirstName).ToArray());
    }

    [DockerFact]
    public async Task Grouped_phone_predicate_matches_any_of_the_four_columns()
    {
        await SeedAsync();
        var db = new SqlDb(_fx.DataSource);

        var results = await SearchAsync(db, "0001", "00001", userId: 1, phonePattern: "555");

        // Jordan (cell_phone) and Jamie (day_phone) match different columns of the OR group.
        // Morgan also matches (home_phone) but is excluded. Ordered by LastName: Doe < Smith.
        Assert.Equal(["Jamie", "Jordan"], results.Select(r => r.FirstName).ToArray());
    }

    [DockerFact]
    public async Task Left_join_nullability_is_preserved_for_a_person_with_no_filter_row()
    {
        await SeedAsync();
        var db = new SqlDb(_fx.DataSource);

        var results = await SearchAsync(db, "0001", "00001", userId: 1, lastNamePattern: "Smith");

        var jordan = results.Single(r => r.FirstName == "Jordan");
        Assert.Null(jordan.ExclInd);
    }

    [DockerFact]
    public async Task Combined_filters_and_result_mapping_are_unchanged_by_which_filters_ran()
    {
        await SeedAsync();
        var db = new SqlDb(_fx.DataSource);

        // Same fixed columns come back whether zero, one, or several optional filters ran --
        // the generated mapper doesn't depend on which `if` branches executed.
        var noFilters = await SearchAsync(db, "0001", "00001", userId: 1);
        var oneFilter = await SearchAsync(db, "0001", "00001", userId: 1, lastNamePattern: "Smith");
        var severalFilters = await SearchAsync(
            db, "0001", "00001", userId: 1,
            lastNamePattern: "Smith", dateOfBirth: new DateOnly(1990, 1, 1), medicalRecordNumber: "MRN1");

        Assert.Equal(4, noFilters.Count);
        Assert.Equal(3, oneFilter.Count);
        Assert.Equal(["Jordan"], severalFilters.Select(r => r.FirstName).ToArray());
    }

    [DockerFact]
    public async Task Cursor_boundary_after_a_null_date_of_birth_returns_only_non_null_and_greater_null_rows()
    {
        await SeedAsync();
        var db = new SqlDb(_fx.DataSource);

        // Ordered by (LastName, FirstName, DateOfBirth, PersonId): Alex Smith (DOB null)
        // sorts before Jordan/Pat Smith (DOB 1990-01-01) within the "Smith" last-name group,
        // since NULL sorts first ascending on SQL Server.
        var firstPage = await SearchAsync(db, "0001", "00001", userId: 1, lastNamePattern: "Smith", pageSize: 1);
        var alex = Assert.Single(firstPage);
        Assert.Equal("Alex", alex.FirstName);
        Assert.Null(alex.DateOfBirth);

        var afterAlex = await SearchAsync(
            db, "0001", "00001", userId: 1, lastNamePattern: "Smith",
            cursor: new PatientSearchCursor(alex.LastName, alex.FirstName, alex.DateOfBirth, alex.PersonId));

        Assert.Equal(["Jordan", "Pat"], afterAlex.Select(r => r.FirstName).ToArray());
    }
}

file sealed class Person : SqlTable<Person>
{
    public Person() : base("person", "dbo") { }

    public SqlColumn<Guid> PersonId { get; } = UniqueIdentifier("person_id").NotNull();
    public SqlColumn<string> PersonNbr { get; } = VarChar("person_nbr", 20).NotNull();
    public SqlColumn<string> FirstName { get; } = VarChar("first_name", 60).NotNull();
    public SqlColumn<string> LastName { get; } = VarChar("last_name", 60).NotNull();
    public SqlColumn<string> MiddleName { get; } = VarChar("middle_name", 25);
    public SqlColumn<DateOnly> DateOfBirth { get; } = Date("date_of_birth");
    public SqlColumn<string> DayPhone { get; } = VarChar("day_phone", 10);
    public SqlColumn<string> HomePhone { get; } = VarChar("home_phone", 10);
    public SqlColumn<string> CellPhone { get; } = VarChar("cell_phone", 10);
    public SqlColumn<string> AltPhone { get; } = VarChar("alt_phone", 10);
    public SqlColumn<string> EmailAddress { get; } = VarChar("email_address", 80);
    public SqlColumn<string> PracticeId { get; } = Char("practice_id", 4).NotNull();
    public SqlColumn<string> EnterpriseId { get; } = Char("enterprise_id", 5).NotNull();
}

file sealed class Patient : SqlTable<Patient>
{
    public Patient() : base("patient", "dbo") { }

    public SqlColumn<Guid> PersonId { get; } = UniqueIdentifier("person_id").NotNull();
    public SqlColumn<string> MedRecNbr { get; } = VarChar("med_rec_nbr", 20);
}

file sealed class UserPersonFilter : SqlTable<UserPersonFilter>
{
    public UserPersonFilter() : base("user_person_filter", "dbo") { }

    public SqlColumn<Guid> PersonId { get; } = UniqueIdentifier("person_id").NotNull();
    public SqlColumn<int> UserId { get; } = Int("user_id").NotNull();
    public SqlColumn<string> InclInd { get; } = Char("incl_ind", 1);
    public SqlColumn<string> ExclInd { get; } = Char("excl_ind", 1);
}
