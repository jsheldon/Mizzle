namespace Mizzle.Tests;

file sealed class Users : PgTable<Users>
{
    public Users() : base("users", "public") { }

    public PgColumn<int> Id { get; } = Identity("id");
    public PgColumn<string> Email { get; } = Text("email");
}

file static class PagingConvert
{
    public static Guid ToGuid(string value) => Guid.Parse(value);

    public static string FromGuid(Guid value) => value.ToString("D");
}

file sealed class LegacyPatients : SqlTable<LegacyPatients>
{
    public LegacyPatients() : base("patient", "dbo") { }

    public SqlColumn<Guid> PersonId { get; } = Char("person_id", 36).Map(PagingConvert.ToGuid, PagingConvert.FromGuid);
    public SqlColumn<string> LastName { get; } = VarChar("last_name", 50).NotNull();
    public SqlColumn<DateOnly> Reviewed { get; } = Date("review_date");
}

public sealed class PagingTests
{
    private static string EmitSql(SelectQuery q)
    {
        var (canonical, values) = Parameterizer.Run(q);
        return new PgEmitter().Emit(canonical, values).Sql;
    }

    [Fact]
    public void Page_sets_limit_and_offset()
    {
        var q = new SelectBuilder()
            .Select(new ColumnRef("u", "email", typeof(string)))
            .From(new FromSource("users", "public", "u"))
            .OrderBy(new ColumnRef("u", "email", typeof(string)))
            .Page(2, 10)
            .Build();
        Assert.Equal(10, q.Limit);
        Assert.Equal(10, q.Offset);
    }

    [Fact]
    public void Page_rejects_page_less_than_one()
    {
        var builder = new SelectBuilder()
            .Select(new ColumnRef("u", "email", typeof(string)))
            .From(new FromSource("users", "public", "u"));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Page(0, 10));
    }

    [Fact]
    public void WindowCount_appends_count_over()
    {
        var query = new SelectQuery(
            Select: [new SelectItem(new ColumnRef("u", "email", typeof(string)), null)],
            From: new FromSource("users", "public", "u"),
            Joins: [],
            Where: null,
            OrderBy: [],
            Limit: null,
            Offset: null,
            Distinct: false,
            With: [],
            RecursiveWith: false,
            UnionAll: [],
            WindowCount: true);
        var sql = new PgEmitter().Emit(query, []);
        Assert.Contains("count(*) OVER()", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("mizzle_total", sql.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void After_requires_order_by()
    {
        var users = new Users();
        var builder = new SelectBuilder()
            .Select(users.Email)
            .From(users.ToFrom());
        var ex = Assert.Throws<InvalidOperationException>(() => builder.After((users.Email, "a@b.com")));
        Assert.Equal("ORDER BY is required for After.", ex.Message);
    }

    [Fact]
    public void After_adds_seek_predicate()
    {
        var users = new Users();
        var q = new SelectBuilder()
            .Select(users.Email)
            .From(users.ToFrom())
            .OrderBy(users.Email.ToRef())
            .After((users.Email, "a@b.com"))
            .Build();
        Assert.NotNull(q.Where);
        Assert.Equal(
            "SELECT \"users\".\"email\" FROM \"public\".\"users\" AS \"users\" WHERE \"users\".\"email\" > $1 ORDER BY \"users\".\"email\"",
            EmitSql(q));
    }

    private static (string Sql, IReadOnlyList<object?> Values) EmitSqlServer(SelectQuery q)
    {
        var (canonical, values) = Parameterizer.Run(q);
        return (new SqlServerEmitter().Emit(canonical, values).Sql, values);
    }

    [Fact]
    public void After_rejects_cursor_column_not_matching_order_by()
    {
        var users = new Users();
        var builder = new SelectBuilder()
            .Select(users.Id, users.Email)
            .From(users.ToFrom())
            .OrderBy(users.Email.ToRef());

        var ex = Assert.Throws<ArgumentException>(() => builder.After((users.Id, 1)));
        Assert.Contains("position 0", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void After_binds_cursor_value_through_storage_converter()
    {
        var p = new LegacyPatients();
        var personId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var q = new SelectBuilder()
            .Select(p.PersonId)
            .From(p.ToFrom())
            .OrderBy(p.PersonId.ToRef())
            .After((p.PersonId, personId))
            .Build();

        var (_, values) = Parameterizer.Run(q);
        Assert.Equal(personId.ToString("D"), Assert.Single(values));
    }

    [Fact]
    public void After_handles_null_cursor_value_ascending()
    {
        var p = new LegacyPatients();
        var q = new SelectBuilder()
            .Select(p.PersonId)
            .From(p.ToFrom())
            .OrderBy(p.Reviewed.ToRef())
            .OrderBy(p.LastName.ToRef())
            .After((p.Reviewed, null), (p.LastName, "Smith"))
            .Build();

        var (sql, _) = EmitSqlServer(q);
        // ASC (NULLS FIRST, SQL Server default): rows after a null review_date are
        // those with a non-null review_date, or another null-review_date row whose
        // last_name is greater -- a plain "> NULL" would silently match nothing.
        Assert.Contains("[review_date] IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[review_date] IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[last_name] >", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void After_handles_null_cursor_value_descending()
    {
        var p = new LegacyPatients();
        var q = new SelectBuilder()
            .Select(p.PersonId)
            .From(p.ToFrom())
            .OrderByDesc(p.Reviewed.ToRef())
            .OrderBy(p.LastName.ToRef())
            .After((p.Reviewed, null), (p.LastName, "Smith"))
            .Build();

        var (sql, _) = EmitSqlServer(q);
        // DESC (NULLS LAST): nothing sorts after a trailing null on this column;
        // only a tie (also null) can continue via the next tie-break column.
        Assert.DoesNotContain("[review_date] IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[review_date] IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[last_name] >", sql, StringComparison.Ordinal);
    }
}
