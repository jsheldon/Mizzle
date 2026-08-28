using System.Text;

namespace Mizzle.Cli.Infrastructure;

internal static class TextNames
{
    public static string ToPascal(string name)
    {
        var sb = new StringBuilder();
        var upper = true;
        foreach (var ch in name)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                upper = true;
                continue;
            }

            sb.Append(upper ? char.ToUpperInvariant(ch) : ch);
            upper = false;
        }

        return sb.Length == 0 ? "Generated" : sb.ToString();
    }

    public static string ToTableClass(string tableName)
    {
        var name = ToPascal(tableName);
        return name.EndsWith("s", StringComparison.Ordinal) && name.Length > 1
            ? name
            : name + "Table";
    }

    public static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
