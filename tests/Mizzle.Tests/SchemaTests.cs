using Mizzle.Schema;

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
        Assert.True(users.Email.IsRequired);
        Assert.True(users.Email.IsUnique);
        Assert.True(users.Id.IsRequired); // PrimaryKey implies required
    }

    [Fact]
    public void Default_and_references_are_recorded()
    {
        var posts = new Posts();
        Assert.True(posts.Status.HasDefault);
        Assert.Equal("draft", posts.Status.DefaultValue);
        Assert.Same(Posts.Referenced.Id, posts.UserId.ReferencedColumn);
    }

    [Fact]
    public void Factories_carry_clr_types_and_length()
    {
        var t = new WideTable();
        Assert.Equal(typeof(DateTimeOffset), t.CreatedAt.ClrType);
        Assert.Equal(typeof(Guid), t.Key.ClrType);
        Assert.Equal(50, t.Code.Length);
        Assert.Equal(typeof(bool), t.Active.ClrType);
        Assert.Equal(typeof(long), t.Big.ClrType);
        var s = new WideSqlTable();
        Assert.Equal(255, s.Email.Length);
        Assert.Null(s.Notes.Length);
        Assert.Equal(typeof(DateTime), s.CreatedAt.ClrType);
        Assert.Equal(typeof(bool), s.Active.ClrType);
        Assert.Equal(typeof(long), s.Big.ClrType);
        Assert.Equal(typeof(Guid), s.Key.ClrType);
    }

    [Fact]
    public void Constraints_hook_exposes_composite_keys()
    {
        var t = new LinkTable();
        var pk = Assert.IsType<CompositePrimaryKey>(Assert.Single(t.Constraints));
        Assert.Equal(2, pk.Columns.Count);
    }
}

file sealed class LinkTable : PgTable<LinkTable>
{
    public LinkTable() : base("links", "public", "l") { }
    public PgColumn<int> UserId { get; } = Integer("user_id");
    public PgColumn<int> RoleId { get; } = Integer("role_id");
    protected override IEnumerable<TableConstraint> DefineConstraints()
        => [new CompositePrimaryKey([UserId, RoleId])];
}

file sealed class WideTable : PgTable<WideTable>
{
    public WideTable() : base("wide", "public", "w") { }
    public PgColumn<DateTimeOffset> CreatedAt { get; } = Timestamptz("created_at");
    public PgColumn<Guid> Key { get; } = Uuid("key");
    public PgColumn<string> Code { get; } = Varchar("code", 50);
    public PgColumn<bool> Active { get; } = Boolean("active");
    public PgColumn<long> Big { get; } = BigInt("big");
}

file sealed class WideSqlTable : SqlTable<WideSqlTable>
{
    public WideSqlTable() : base("wide", "dbo", "w") { }
    public SqlColumn<string> Email { get; } = NVarChar("email", 255);
    public SqlColumn<string> Notes { get; } = NVarCharMax("notes");
    public SqlColumn<DateTime> CreatedAt { get; } = DateTime2("created_at");
    public SqlColumn<bool> Active { get; } = Bit("active");
    public SqlColumn<long> Big { get; } = BigInt("big");
    public SqlColumn<Guid> Key { get; } = UniqueIdentifier("key");
}
