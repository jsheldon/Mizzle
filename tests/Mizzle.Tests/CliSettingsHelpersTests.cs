using Mizzle.Cli.Commands;
using Mizzle.Cli.Infrastructure;
using Mizzle.Cli.Schema;

namespace Mizzle.Tests;

public sealed class CliSettingsHelpersTests
{
    [Fact]
    public void Ensure_requested_tables_found_throws_when_all_requested_tables_are_missing()
    {
        var ex = Assert.Throws<CliFailure>(() =>
            SettingsHelpers.EnsureRequestedTablesFound(
                ["missing"],
                [new TableInfo("public", "users", [])]));

        Assert.Equal("MZCLI015", ex.Code);
        Assert.Contains("None of the requested tables were found", ex.Message, StringComparison.Ordinal);
        Assert.Contains("missing", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ensure_requested_tables_found_throws_when_some_requested_tables_are_missing()
    {
        var ex = Assert.Throws<CliFailure>(() =>
            SettingsHelpers.EnsureRequestedTablesFound(
                ["users", "posts"],
                [new TableInfo("public", "users", [])]));

        Assert.Equal("MZCLI015", ex.Code);
        Assert.Contains("Some requested tables were not found", ex.Message, StringComparison.Ordinal);
        Assert.Contains("posts", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ensure_requested_tables_found_is_case_insensitive()
    {
        SettingsHelpers.EnsureRequestedTablesFound(
            ["USERS"],
            [new TableInfo("public", "users", [])]);
    }

    [Fact]
    public void Missing_requested_tables_returns_only_missing_names()
    {
        var missing = SettingsHelpers.MissingRequestedTables(
            ["users", "posts", "tags"],
            [
                new TableInfo("public", "users", []),
                new TableInfo("public", "tags", []),
            ]);

        Assert.Equal(["posts"], missing);
    }
}
