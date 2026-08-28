using Mizzle.Cli.Infrastructure;
using System.Text;
using System.Text.RegularExpressions;

namespace Mizzle.Cli.SqlTranslation;

internal static class SqlTranslator
{
    public static string Translate(ProviderKind provider, string sql)
    {
        _ = provider;
        sql = sql.Trim().TrimEnd(';');
        if (Regex.IsMatch(sql, @"\b(group\s+by|having|with|union|over\s*\(|select\s+distinct)\b", RegexOptions.IgnoreCase))
        {
            throw new CliFailure(
                "MZCLI060",
                "This SQL uses syntax translate-query does not support yet.",
                "Start with a simple SELECT/FROM/WHERE/ORDER BY/LIMIT query or add Mizzle support for this construct.");
        }

        var match = Regex.Match(
            sql,
            @"^select\s+(?<select>.+?)\s+from\s+(?<from>[\w\.\[\]""]+)(?<rest>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success)
        {
            throw new CliFailure("MZCLI061", "Could not parse SQL.", "The first version supports simple SELECT ... FROM ... queries only.");
        }

        var columns = match.Groups["select"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (columns.Any(c => c.Contains('(') || c.Contains('*')))
        {
            throw new CliFailure("MZCLI062", "Computed columns and SELECT * are not translatable yet.", "Select named columns so Mizzle can map them to table properties.");
        }

        var tableName = CleanName(match.Groups["from"].Value.Split('.').Last());
        var variable = char.ToLowerInvariant(tableName[0]) + tableName[1..];
        var sb = new StringBuilder();
        sb.AppendLine($"var {variable} = new {TextNames.ToTableClass(tableName)}();");
        sb.AppendLine();
        sb.AppendLine($"var rows = await db.Select({string.Join(", ", columns.Select(c => variable + "." + TextNames.ToPascal(CleanName(c.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Split('.').Last()))))})");
        sb.AppendLine($"    .From({variable})");

        var rest = match.Groups["rest"].Value;
        var where = Regex.Match(rest, @"\bwhere\s+(?<where>.+?)(\border\s+by\b|\blimit\b|\boffset\b|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (where.Success)
        {
            foreach (var part in Regex.Split(where.Groups["where"].Value.Trim(), @"\s+and\s+", RegexOptions.IgnoreCase))
            {
                var eq = Regex.Match(part.Trim(), @"^(?<col>[\w\.]+)\s*=\s*(?<value>[@:\w]+)$");
                if (!eq.Success)
                {
                    throw new CliFailure("MZCLI063", $"Unsupported WHERE predicate '{part.Trim()}'.", "Only column = parameter joined by AND is supported right now.");
                }

                sb.AppendLine($"    .Where({variable}.{TextNames.ToPascal(eq.Groups["col"].Value.Split('.').Last())}.Eq({CleanParameter(eq.Groups["value"].Value)}))");
            }
        }

        var order = Regex.Match(rest, @"\border\s+by\s+(?<order>[\w\.]+)(\s+(?<dir>desc|asc))?", RegexOptions.IgnoreCase);
        if (order.Success)
        {
            var method = string.Equals(order.Groups["dir"].Value, "desc", StringComparison.OrdinalIgnoreCase) ? "OrderByDesc" : "OrderBy";
            sb.AppendLine($"    .{method}({variable}.{TextNames.ToPascal(order.Groups["order"].Value.Split('.').Last())})");
        }

        var limit = Regex.Match(rest, @"\blimit\s+(?<limit>\d+)", RegexOptions.IgnoreCase);
        if (limit.Success)
        {
            sb.AppendLine($"    .Limit({limit.Groups["limit"].Value})");
        }

        sb.AppendLine("    .ToListAsync<Row>();");
        return sb.ToString();
    }

    private static string CleanName(string value)
        => value.Trim().Trim('[', ']', '"');

    private static string CleanParameter(string value)
    {
        var clean = value.Trim().TrimStart('@', ':');
        return clean.Length == 0 ? "value" : clean;
    }
}
