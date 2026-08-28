using Spectre.Console;

namespace Mizzle.Cli.Infrastructure;

internal static class ConsoleText
{
    public static string Escape(string value) => Markup.Escape(value);
}
