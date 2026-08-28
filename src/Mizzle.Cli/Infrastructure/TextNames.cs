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

        if (sb.Length == 0)
        {
            return "Generated";
        }

        // A C# identifier cannot start with a digit; legacy schemas do
        // (2fa_enabled, 1st_contact), and the scaffolded class would not compile.
        if (char.IsDigit(sb[0]))
        {
            sb.Insert(0, '_');
        }

        return sb.ToString();
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
