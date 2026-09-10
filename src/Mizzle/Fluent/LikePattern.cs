using System.Text;

namespace Mizzle.Fluent;

/// <summary>
///     Builds an escaped LIKE pattern from literal search text -- <c>Like</c>/<c>ILike</c> stay
///     faithful pattern operators (they never reinterpret their input), so turning "user typed this
///     text" into "match it as a substring/prefix/exact value" is a separate, explicit step.
///     Escape input through here before handing it to <c>Like(pattern, escape)</c>/<c>ILike</c>.
/// </summary>
public static class LikePattern
{
    /// <summary>Matches <paramref name="text"/> anywhere in the column (a %text% pattern).</summary>
    public static string Contains(string text, char escape) => $"%{Escape(text, escape)}%";

    /// <summary>Matches <paramref name="text"/> at the start of the column (a text% pattern).</summary>
    public static string StartsWith(string text, char escape) => $"{Escape(text, escape)}%";

    /// <summary>Matches <paramref name="text"/> exactly, with its own % and _ treated as literal.</summary>
    public static string Exact(string text, char escape) => Escape(text, escape);

    private static string Escape(string text, char escape)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch == escape || ch == '%' || ch == '_')
            {
                sb.Append(escape);
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }
}
