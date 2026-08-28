namespace Mizzle.Tests;

file sealed class Users : PgTable<Users>
{
    public Users() : base("users", "public") { }
    public PgColumn<int> Id { get; } = Identity("id").PrimaryKey();
    public PgColumn<string> Email { get; } = Text("email").NotNull();
}

file sealed class LegacyCodes : PgTable<LegacyCodes>
{
    public LegacyCodes() : base("legacy_codes", "public") { }

    public PgColumn<Guid> Code { get; } = Text("code").Map(GuidConvert.ToGuid, GuidConvert.FromGuid);
}

file static class GuidConvert
{
    public static Guid ToGuid(string value) => Guid.Parse(value);

    public static string FromGuid(Guid value) => value.ToString("D");
}

file sealed class DataReaderExecutor : IQueryExecutor
{
    private readonly System.Data.DataTable _table;

    public DataReaderExecutor(System.Data.DataTable table) => _table = table;

    public Query? Captured { get; private set; }

    public Task<IReadOnlyList<T>> QueryAsync<T>(Query q, Func<System.Data.Common.DbDataReader, T> m, QueryOptions? o, CancellationToken c)
    {
        Captured = q;
        using var reader = _table.CreateDataReader();
        var rows = new List<T>();
        while (reader.Read())
        {
            rows.Add(m(reader));
        }

        return Task.FromResult<IReadOnlyList<T>>(rows);
    }

    public Task<int> ExecuteAsync(Query q, QueryOptions? o, CancellationToken c) => Task.FromResult(0);

    public IAsyncEnumerable<T> StreamAsync<T>(Query q, Func<System.Data.Common.DbDataReader, T> m, QueryOptions? o, CancellationToken c)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<T>> QueryPrecompiledAsync<T>(string sql, Query q, Func<System.Data.Common.DbDataReader, T> m, QueryOptions? o, CancellationToken c)
        => QueryAsync(q, m, o, c);
}

file sealed record InsertedUser(int Id, string Email);

file sealed record InsertedAlias(int UserId);

public sealed class WriteBuilderTests
{
    private static (string Sql, IReadOnlyList<object?> Values) EmitPg(Query q)
    {
        var (canonical, values) = Parameterizer.Run(q);
        return (new PgEmitter().Emit(canonical, values).Sql, values);
    }

    private static SelectQuery StagingSelect() => new(
        Select: [new SelectItem(new ColumnRef("s", "email", typeof(string)), null)],
        From: new FromSource("staging", "public", "s"),
        Joins: [], Where: null, OrderBy: [], Limit: null, Offset: null,
        Distinct: false, With: [], RecursiveWith: false, UnionAll: []);

    [Fact]
    public void Insert_values_returning_builds_expected_pg_sql()
    {
        var users = new Users();
        var b = new InsertBuilder(users)
            .Value(users.Email, "a@b.com")
            .Returning(users.Id);
        var (sql, values) = EmitPg(b.Build());
        Assert.Equal(
            "INSERT INTO \"public\".\"users\" (\"email\") VALUES ($1) RETURNING \"users\".\"id\"",
            sql);
        Assert.Equal(["a@b.com"], values);
    }

    [Fact]
    public void Insert_two_rows_builds_two_value_tuples()
    {
        var users = new Users();
        var b = new InsertBuilder(users)
            .Value(users.Email, "a@b.com")
            .NewRow()
            .Value(users.Email, "c@d.com");
        var (sql, values) = EmitPg(b.Build());
        Assert.Equal(
            "INSERT INTO \"public\".\"users\" (\"email\") VALUES ($1), ($2)",
            sql);
    }

    [Fact]
    public void Insert_select_builds_insert_from_select()
    {
        var users = new Users();
        var b = new InsertBuilder(users).Select(StagingSelect(), users.Email);
        var (sql, values) = EmitPg(b.Build());
        Assert.Equal(
            "INSERT INTO \"public\".\"users\" (\"email\") SELECT \"s\".\"email\" FROM \"public\".\"staging\" AS \"s\"",
            sql);
    }

    [Fact]
    public void Insert_with_values_then_select_throws()
    {
        var users = new Users();
        var b = new InsertBuilder(users).Value(users.Email, "x");
        Assert.Throws<InvalidOperationException>(() => b.Select(StagingSelect(), users.Email));
    }

    [Fact]
    public void Insert_row_with_different_columns_throws()
    {
        var users = new Users();
        var b = new InsertBuilder(users)
            .Value(users.Email, "a@b.com")
            .NewRow();
        Assert.Throws<InvalidOperationException>(() => b.Value(users.Id, 5).Build());
    }

    [Fact]
    public async Task Insert_returning_scalar_maps_typed_result()
    {
        var users = new Users();
        var data = new System.Data.DataTable();
        data.Columns.Add("id", typeof(int));
        data.Rows.Add(42);
        var exec = new DataReaderExecutor(data);

        var id = await new InsertBuilder(users, exec)
            .Value(users.Email, "a@b.com")
            .Returning(users.Id)
            .SingleAsync<int>();

        Assert.Equal(42, id);
        var query = Assert.IsType<InsertQuery>(exec.Captured);
        Assert.Single(query.Returning);
    }

    [Fact]
    public async Task Insert_returning_record_maps_by_normalized_member_name()
    {
        var users = new Users();
        var data = new System.Data.DataTable();
        data.Columns.Add("id", typeof(int));
        data.Columns.Add("email", typeof(string));
        data.Rows.Add(7, "a@b.com");
        var exec = new DataReaderExecutor(data);

        var row = await new InsertBuilder(users, exec)
            .Value(users.Email, "a@b.com")
            .Returning(users.Id, users.Email)
            .SingleAsync<InsertedUser>();

        Assert.Equal(new InsertedUser(7, "a@b.com"), row);
    }

    [Fact]
    public async Task Insert_returning_respects_column_projection_alias()
    {
        var users = new Users();
        var data = new System.Data.DataTable();
        data.Columns.Add("UserId", typeof(int));
        data.Rows.Add(42);
        var exec = new DataReaderExecutor(data);

        var row = await new InsertBuilder(users, exec)
            .Value(users.Email, "a@b.com")
            .Returning(users.Id.As("UserId"))
            .SingleAsync<InsertedAlias>();

        Assert.Equal(42, row.UserId);
        var query = Assert.IsType<InsertQuery>(exec.Captured);
        Assert.Equal("UserId", Assert.Single(query.Returning).Alias);
    }

    [Fact]
    public async Task Insert_returning_mapped_column_uses_read_converter()
    {
        var codes = new LegacyCodes();
        var expected = Guid.NewGuid();
        var data = new System.Data.DataTable();
        data.Columns.Add("code", typeof(string));
        data.Rows.Add(expected.ToString("D"));
        var exec = new DataReaderExecutor(data);

        var actual = await new InsertBuilder(codes, exec)
            .Value(codes.Code, expected)
            .Returning(codes.Code)
            .SingleAsync<Guid>();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Insert_typed_projection_requires_returning()
    {
        var users = new Users();
        var exec = new DataReaderExecutor(new System.Data.DataTable());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InsertBuilder(users, exec)
                .Value(users.Email, "a@b.com")
                .ToListAsync<InsertedUser>());

        Assert.Equal("Typed insert projection requires Returning(...).", ex.Message);
    }

    [Fact]
    public void Delete_where_returning_builds_expected_pg_sql()
    {
        var users = new Users();
        var b = new DeleteBuilder(users)
            .Where(users.Email, "a@b.com")
            .Returning(users.Id);
        var (sql, values) = EmitPg(b.Build());
        Assert.Equal(
            "DELETE FROM \"public\".\"users\" AS \"users\" WHERE \"users\".\"email\" = $1 RETURNING \"users\".\"id\"",
            sql);
    }

    [Fact]
    public void Update_returning_flows_into_query()
    {
        var users = new Users();
        var b = new UpdateBuilder(users)
            .Set(users.Email, "new@b.com")
            .Where(users.Id, 1)
            .Returning(users.Email);
        var q = b.Build();
        var item = Assert.Single(q.Returning);
        Assert.Equal(new ColumnRef("users", "email", typeof(string)), item.Expr);
    }
}
