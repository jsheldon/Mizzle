namespace Mizzle.Tests;

file sealed class Users : PgTable<Users>
{
    public Users() : base("users", "public", "u") { }
    public PgColumn<int> Id { get; } = Identity("id");
    public PgColumn<string> Email { get; } = Text("email");
}

file sealed class SqlUsers : SqlTable<SqlUsers>
{
    public SqlUsers() : base("users", "dbo", "u") { }
    public SqlColumn<string> Email { get; } = NVarChar("email", 255);
}

public sealed class SchemaTests
{
    [Fact]
    public void Pg_column_is_not_sql_column()
    {
        Assert.False(typeof(PgColumn<string>).IsAssignableFrom(typeof(SqlColumn<string>)));
        Assert.False(typeof(SqlColumn<string>).IsAssignableFrom(typeof(PgColumn<string>)));
    }

    [Fact]
    public void Pg_table_builds_column_ref_with_db_name()
    {
        var users = new Users();
        Assert.Equal("email", users.Email.Name);
        Assert.Equal(DialectKind.Postgres, users.Email.Dialect);
        Assert.Equal(new ColumnRef("u", "email", typeof(string)), users.Email.ToRef());
    }
}
