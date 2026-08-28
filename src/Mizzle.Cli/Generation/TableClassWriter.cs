using Mizzle.Cli.Infrastructure;
using Mizzle.Cli.Schema;
using System.Text;

namespace Mizzle.Cli.Generation;

internal sealed record GeneratedFile(string FileName, string Source);

internal static class TableClassWriter
{
    public static GeneratedFile Write(ProviderKind provider, string ns, TableInfo table)
    {
        var className = TextNames.ToTableClass(table.Name);
        var baseType = provider == ProviderKind.Postgres ? $"PgTable<{className}>" : $"SqlTable<{className}>";
        var columnType = provider == ProviderKind.Postgres ? "PgColumn" : "SqlColumn";
        var usingNs = provider == ProviderKind.Postgres ? "Mizzle.Postgres" : "Mizzle.SqlServer";

        var sb = new StringBuilder();
        sb.AppendLine($"using {usingNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {className} : {baseType}");
        sb.AppendLine("{");
        sb.AppendLine($"    public {className}() : base(\"{TextNames.Escape(table.Name)}\", \"{TextNames.Escape(table.Schema)}\") {{ }}");
        sb.AppendLine();
        foreach (var column in table.Columns)
        {
            var mapping = TypeMappings.Resolve(provider, column);
            var modifiers = new List<string>();
            if (column.IsPrimaryKey)
            {
                modifiers.Add("PrimaryKey()");
            }
            else if (!column.IsNullable)
            {
                modifiers.Add("NotNull()");
            }

            var factory = mapping.NeedsLength && column.Length is int length
                ? $"{mapping.Factory}(\"{TextNames.Escape(column.Name)}\", {length})"
                : $"{mapping.Factory}(\"{TextNames.Escape(column.Name)}\")";
            if (mapping.NeedsLength && column.Length is null)
            {
                factory = provider == ProviderKind.SqlServer && mapping.Factory == "NVarChar"
                    ? $"NVarCharMax(\"{TextNames.Escape(column.Name)}\")"
                    : throw new CliFailure(
                        "MZCLI022",
                        $"Type '{column.StoreType}' on {column.Schema}.{column.Table}.{column.Name} requires a length Mizzle can express.",
                        "Add a max-length factory or scaffold this column by hand.");
            }

            var chain = modifiers.Count == 0 ? factory : factory + "." + string.Join(".", modifiers);
            sb.AppendLine($"    public {columnType}<{mapping.ClrType}> {TextNames.ToPascal(column.Name)} {{ get; }} = {chain};");
        }

        sb.AppendLine("}");
        return new GeneratedFile(className + ".cs", sb.ToString());
    }
}
