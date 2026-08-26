namespace Mizzle.Tests;

file sealed class Users : PgTable<Users>
{
    public Users() : base("users", "public", "u") { }
    public PgColumn<int> Id { get; } = Identity("id").PrimaryKey();
    public PgColumn<string> Email { get; } = Text("email").NotNull().Unique();
}

file sealed class SqlUsers : SqlTable<SqlUsers>
{
    public SqlUsers() : base("users", "dbo", "u") { }
    public SqlColumn<string> Email { get; } = NVarChar("email", 255);
}

file sealed class Posts : PgTable<Posts>
{
    private static readonly Users UsersRef = new();
    public Posts() : base("posts", "public", "p") { }
    public PgColumn<int> Id { get; } = Identity("id").PrimaryKey();
    public PgColumn<int> UserId { get; } = Integer("user_id").References(UsersRef.Id);
    public PgColumn<string> Status { get; } = Text("status").NotNull().Default("draft");

    public static Users Referenced => UsersRef;
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

    [Fact]
    public void Modifier_chain_sets_metadata_and_returns_pg_column()
    {
        var users = new Users();
        Assert.True(users.Id.IsPrimaryKey);
        Assert.True(users.Email.IsNotNull);
        Assert.True(users.Email.IsUnique);
        Assert.False(users.Id.IsNotNull);
    }

    [Fact]
    public void Default_and_references_are_recorded()
    {
        var posts = new Posts();
        Assert.True(posts.Status.HasDefault);
        Assert.Equal("draft", posts.Status.DefaultValue);
        Assert.Same(Posts.Referenced.Id, posts.UserId.ReferencedColumn);
    }
}
