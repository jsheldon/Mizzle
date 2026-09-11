namespace Mizzle.Tests;

file sealed class SqlSlots : SqlTable<SqlSlots>
{
    public SqlSlots() : base("appt_slots", "dbo") { }
    public SqlColumn<string> PracticeId { get; } = Char("practice_id", 4).NotNull();
    public SqlColumn<Guid> CategoryId { get; } = UniqueIdentifier("category_id");
    public SqlColumn<int> AppCount { get; } = Int("appt_count");
    public SqlColumn<int> CategoryCount { get; } = Int("category_count");
}

file sealed class SqlCategoryMembers : SqlTable<SqlCategoryMembers>
{
    public SqlCategoryMembers() : base("category_members", "dbo") { }
    public SqlColumn<Guid> CategoryId { get; } = UniqueIdentifier("category_id").NotNull();
    public SqlColumn<Guid> EventId { get; } = UniqueIdentifier("event_id");
}

file sealed class PgSlots : PgTable<PgSlots>
{
    public PgSlots() : base("appt_slots", "public") { }
    public PgColumn<int> AppCount { get; } = Integer("appt_count");
}

file sealed class PgCategoryMembers : PgTable<PgCategoryMembers>
{
    public PgCategoryMembers() : base("category_members", "public") { }
    public PgColumn<Guid> EventId { get; } = Uuid("event_id");
}

// UPDATE...FROM...JOIN: SQL-Server-only today (Postgres would need a self-join rewrite to
// express the updated table as one side of the join, not implemented). Arithmetic (+/-)
// renders identically on both dialects and isn't itself join-restricted.
public sealed class UpdateJoinTests
{
    private static CompiledSql Compile(ISqlEmitter emitter, UpdateBuilder builder)
    {
        var (canonical, values) = Parameterizer.Run(builder.Build());
        return emitter.Emit(canonical, values);
    }

    [Fact]
    public void SqlServer_left_join_with_guarded_increment_renders_expected_sql()
    {
        var s = new SqlSlots();
        var cm = new SqlCategoryMembers();
        var eventId = Guid.NewGuid();

        var sql = Compile(new SqlServerEmitter(), new UpdateBuilder(s)
            .LeftJoin(cm, Sql.And(cm.CategoryId.Eq(s.CategoryId), cm.EventId.Eq(eventId)))
            .Set(s.AppCount, Sql.Add(Sql.Coalesce(s.AppCount, Sql.Value(0)), Sql.Value(1)))
            .Set(s.CategoryCount,
                Sql.Case(Sql.When(Sql.IsNotNull(cm.EventId),
                        Sql.Add(Sql.Coalesce(s.CategoryCount, Sql.Value(0)), Sql.Value(1))))
                    .Else(s.CategoryCount))
            .Where(s.PracticeId, "0001"));

        Assert.Equal(
            "UPDATE [appt_slots] SET [appt_slots].[appt_count] = coalesce([appt_slots].[appt_count], @p0) + @p1, "
            + "[appt_slots].[category_count] = CASE WHEN [category_members].[event_id] IS NOT NULL "
            + "THEN coalesce([appt_slots].[category_count], @p2) + @p3 ELSE [appt_slots].[category_count] END "
            + "FROM [dbo].[appt_slots] AS [appt_slots] "
            + "LEFT JOIN [dbo].[category_members] AS [category_members] "
            + "ON ([category_members].[category_id] = [appt_slots].[category_id] "
            + "AND [category_members].[event_id] = @p4) "
            + "WHERE [appt_slots].[practice_id] = @p5",
            sql.Sql);
        Assert.Equal<object?[]>([0, 1, 0, 1, eventId, "0001"], [.. sql.Parameters]);
    }

    [Fact]
    public void SqlServer_inner_join_renders_inner_join_keyword()
    {
        var s = new SqlSlots();
        var cm = new SqlCategoryMembers();

        var sql = Compile(new SqlServerEmitter(), new UpdateBuilder(s)
            .InnerJoin(cm, cm.CategoryId.Eq(s.CategoryId))
            .Set(s.AppCount, 1));

        Assert.Equal(
            "UPDATE [appt_slots] SET [appt_slots].[appt_count] = @p0 "
            + "FROM [dbo].[appt_slots] AS [appt_slots] "
            + "INNER JOIN [dbo].[category_members] AS [category_members] "
            + "ON [category_members].[category_id] = [appt_slots].[category_id]",
            sql.Sql);
    }

    [Fact]
    public void SqlServer_update_without_joins_is_unchanged()
    {
        var s = new SqlSlots();
        var sql = Compile(new SqlServerEmitter(), new UpdateBuilder(s).Set(s.AppCount, 1));

        Assert.Equal("UPDATE [dbo].[appt_slots] SET [appt_count] = @p0", sql.Sql);
    }

    [Fact]
    public void Postgres_update_with_join_throws_unsupported_feature()
    {
        var s = new PgSlots();
        var cm = new PgCategoryMembers();

        var ex = Assert.Throws<UnsupportedFeatureException>(() => Compile(new PgEmitter(), new UpdateBuilder(s)
            .LeftJoin(cm, cm.EventId.Eq(Guid.NewGuid()))
            .Set(s.AppCount, 1)));

        Assert.Equal(Feature.UpdateJoin, ex.Feature);
    }

    [Fact]
    public void SqlServer_subtract_renders_as_arithmetic()
    {
        var s = new SqlSlots();
        var sql = Compile(new SqlServerEmitter(), new UpdateBuilder(s)
            .Set(s.AppCount, Sql.Subtract(Sql.Coalesce(s.AppCount, Sql.Value(0)), Sql.Value(1))));

        Assert.Equal(
            "UPDATE [dbo].[appt_slots] SET [appt_count] = coalesce([appt_slots].[appt_count], @p0) - @p1",
            sql.Sql);
    }

    [Fact]
    public void Postgres_add_renders_as_arithmetic()
    {
        var s = new PgSlots();
        var sql = Compile(new PgEmitter(), new UpdateBuilder(s)
            .Set(s.AppCount, Sql.Add(Sql.Coalesce(s.AppCount, Sql.Value(0)), Sql.Value(1))));

        Assert.Equal(
            "UPDATE \"public\".\"appt_slots\" AS \"appt_slots\" "
            + "SET \"appt_count\" = coalesce(\"appt_slots\".\"appt_count\", $1) + $2",
            sql.Sql);
    }
}
