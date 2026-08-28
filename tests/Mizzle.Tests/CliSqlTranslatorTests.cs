using Mizzle.Cli.Infrastructure;
using Mizzle.Cli.SqlTranslation;

namespace Mizzle.Tests;

public sealed class CliSqlTranslatorTests
{
    [Fact]
    public void Translates_simple_select_where_order_limit()
    {
        var translated = SqlTranslator.Translate(
            ProviderKind.Postgres,
            "select id, email from public.users where id = @id order by email limit 10");

        Assert.Contains("var users = new Users();", translated, StringComparison.Ordinal);
        Assert.Contains("db.Select(users.Id, users.Email)", translated, StringComparison.Ordinal);
        Assert.Contains(".From(users)", translated, StringComparison.Ordinal);
        Assert.Contains(".Where(users.Id.Eq(id))", translated, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(users.Email)", translated, StringComparison.Ordinal);
        Assert.Contains(".Limit(10)", translated, StringComparison.Ordinal);
        Assert.Contains(".ToListAsync<Row>();", translated, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_computed_select_fails_with_clear_code()
    {
        var ex = Assert.Throws<CliFailure>(() => SqlTranslator.Translate(ProviderKind.Postgres, "select count(*) from public.users"));

        Assert.Equal("MZCLI062", ex.Code);
        Assert.Contains("Computed columns", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_where_predicate_fails_with_clear_code()
    {
        var ex = Assert.Throws<CliFailure>(() => SqlTranslator.Translate(ProviderKind.Postgres, "select id from users where id > @id"));

        Assert.Equal("MZCLI063", ex.Code);
        Assert.Contains("Only column = parameter", ex.Hint, StringComparison.Ordinal);
    }
}
