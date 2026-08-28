using Mizzle.Cli.Infrastructure;
using Spectre.Console;

namespace Mizzle.Tests;

public sealed class CliConsoleTextTests
{
    [Fact]
    public void Escapes_square_brackets_for_spectre_markup()
    {
        var escaped = ConsoleText.Escape("byte[]");

        _ = new Markup(escaped);
        Assert.Equal("byte[[]]", escaped);
    }
}
