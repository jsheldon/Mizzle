using Mizzle.Cli.Infrastructure;

namespace Mizzle.Tests;

public sealed class CliTextNamesTests
{
    [Theory]
    [InlineData("person_id", "PersonId")]
    [InlineData("address_line_1", "AddressLine1")]
    [InlineData("mstr_list_item_desc", "MstrListItemDesc")]
    public void Pascal_cases_snake_case_names(string input, string expected)
        => Assert.Equal(expected, TextNames.ToPascal(input));

    [Theory]
    [InlineData("2fa_enabled", "_2faEnabled")]
    [InlineData("1st_contact", "_1stContact")]
    [InlineData("123", "_123")]
    public void Prefixes_names_that_would_start_with_a_digit(string input, string expected)
    {
        // C# identifiers cannot start with a digit -- without the prefix the
        // scaffolded class does not compile.
        var result = TextNames.ToPascal(input);
        Assert.Equal(expected, result);
        Assert.False(char.IsDigit(result[0]));
    }

    [Fact]
    public void Falls_back_when_nothing_survives()
        => Assert.Equal("Generated", TextNames.ToPascal("___"));
}
