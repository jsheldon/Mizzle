using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Mizzle.Generators;

[Generator]
public sealed class SchemaGenerator : IIncrementalGenerator
{
    internal static readonly DiagnosticDescriptor DialectMismatch = new(
        "MIZ001",
        "Column dialect does not match table",
        "Column '{0}' is not valid on this table dialect",
        "Mizzle",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor InvalidConverter = new(
        "MIZ008",
        "Column converter is not statically bakeable",
        "Column '{0}': Map arguments must be static method references so generated mappers can call them",
        "Mizzle",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor NullableConverterResult = new(
        "MIZ009",
        "Column converter result is nullable",
        "Column '{0}': Map result type '{1}' is nullable. A column's type argument is the storage type, "
            + "so express nullability by omitting NotNull() and map to '{2}' instead.",
        "Mizzle",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly Dictionary<string, DiagnosticDescriptor> ColumnDescriptors = new()
    {
        ["MIZ008"] = InvalidConverter,
        ["MIZ009"] = NullableConverterResult,
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var tables = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (ctx, _) => Transform(ctx))
            .Where(static table => table is not null);

        context.RegisterSourceOutput(tables, static (spc, table) => Generate(spc, table!));
    }

    private static TableModel? Transform(GeneratorSyntaxContext context)
    {
        var syntax = (ClassDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(syntax) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        if (symbol.IsAbstract || !IsDialectTable(symbol, out var postgres))
        {
            return null;
        }

        var columns = new List<ColumnModel>();
        var mismatches = new List<string>();
        var converterErrors = new List<(string Id, Location Location, string[] Args)>();
        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsStatic || member.Type is not INamedTypeSymbol type)
            {
                continue;
            }

            if (!TryColumn(type, out var clr, out var isSqlColumn))
            {
                continue;
            }

            if (postgres == isSqlColumn)
            {
                mismatches.Add(member.Name);
                continue;
            }

            var isIdentity = FactoryName(member) == "Identity";
            var modifiers = TableFacts.ModifierNames(member);
            var isRequired = isIdentity || modifiers.Contains("NotNull") || modifiers.Contains("PrimaryKey");
            var mapStatus = TableFacts.GetMapInfo(
                member, context.SemanticModel.Compilation, out var converterFq, out var storageReader, out var mapLocation);
            var location = mapLocation ?? member.Locations.FirstOrDefault() ?? Location.None;
            if (mapStatus == MapStatus.Invalid)
            {
                converterErrors.Add(("MIZ008", location, [member.Name]));
                continue;
            }

            if (mapStatus == MapStatus.NullableResult)
            {
                converterErrors.Add(("MIZ009", location, [
                    member.Name,
                    clr.ToDisplayString(),
                    TableFacts.NonNullableResult(clr).ToDisplayString()
                ]));
                continue;
            }

            columns.Add(mapStatus == MapStatus.Valid
                ? new ColumnModel(member.Name, ToCSharpType(clr), storageReader!, isIdentity, isRequired, converterFq)
                : new ColumnModel(member.Name, ToCSharpType(clr), ReaderMethod(clr), isIdentity, isRequired));
        }

        if (columns.Count == 0 && mismatches.Count == 0 && converterErrors.Count == 0)
        {
            return null;
        }

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? "Mizzle.Generated"
            : symbol.ContainingNamespace.ToDisplayString();

        return new TableModel(ns, symbol.Name, Singular(symbol.Name), columns, mismatches, converterErrors);
    }

    private static void Generate(SourceProductionContext context, TableModel table)
    {
        foreach (var (id, location, args) in table.ConverterErrors)
        {
            context.ReportDiagnostic(Diagnostic.Create(ColumnDescriptors[id], location, [..args.Cast<object?>()]));
        }

        foreach (var name in table.Mismatches)
        {
            context.ReportDiagnostic(Diagnostic.Create(DialectMismatch, Location.None, name));
        }

        if (table.Columns.Count == 0)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.Append("namespace ");
        sb.AppendLine(table.Namespace);
        sb.AppendLine("{");
        sb.Append("    public sealed record ");
        sb.Append(table.Singular);
        sb.Append('(');
        sb.Append(string.Join(", ", table.Columns.Select(c => MemberType(c) + " " + c.Name)));
        sb.AppendLine(");");

        var insertables = table.Columns.Where(c => !c.IsIdentity).ToList();
        sb.Append("    public sealed record New");
        sb.Append(table.Singular);
        if (insertables.Count == 0)
        {
            sb.AppendLine(";");
        }
        else
        {
            sb.Append('(');
            sb.Append(string.Join(", ", insertables.Select(c => MemberType(c) + " " + c.Name)));
            sb.AppendLine(");");
        }

        sb.Append("    public static class ");
        sb.Append(table.TableName);
        sb.AppendLine("Mapper");
        sb.AppendLine("    {");
        sb.Append("        public static ");
        sb.Append(table.Singular);
        sb.AppendLine(" Read(global::System.Data.Common.DbDataReader r)");
        sb.Append("            => new(");
        sb.Append(string.Join(", ", table.Columns.Select(ReadCall)));
        sb.AppendLine(");");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        var hint = table.Namespace + "." + table.TableName + ".Mizzle.g.cs";
        context.AddSource(hint, SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static bool IsDialectTable(INamedTypeSymbol symbol, out bool postgres)
        => TableFacts.IsDialectTable(symbol, out postgres);

    private static bool TryColumn(INamedTypeSymbol type, out ITypeSymbol clr, out bool isSqlColumn)
        => TableFacts.TryColumn(type, out clr, out isSqlColumn);

    private static string? FactoryName(IPropertySymbol member)
        => TableFacts.FactoryName(member);

    private static string ToCSharpType(ITypeSymbol type)
        => TableFacts.ToCSharpType(type);

    private static string ReaderMethod(ITypeSymbol type) => TableFacts.ReaderCall(type);

    private static string MemberType(ColumnModel column)
        => column.IsRequired ? column.ClrType : column.ClrType + "?";

    private static string ReadCall(ColumnModel column, int ordinal)
    {
        var read = column.ConverterFq is null
            ? $"r.{column.Reader}({ordinal})"
            : $"{column.ConverterFq}(r.{column.Reader}({ordinal}))";
        return column.IsRequired
            ? read
            : $"r.IsDBNull({ordinal}) ? ({column.ClrType}?)null : {read}";
    }

    private static string Singular(string tableName)
    {
        if (tableName.Length > 1 && (tableName.EndsWith("s", StringComparison.Ordinal) || tableName.EndsWith("S", StringComparison.Ordinal)))
        {
            return tableName.Substring(0, tableName.Length - 1);
        }

        return tableName;
    }

    private sealed class TableModel
    {
        public TableModel(
            string ns,
            string tableName,
            string singular,
            List<ColumnModel> columns,
            List<string> mismatches,
            List<(string Id, Location Location, string[] Args)> converterErrors)
        {
            Namespace = ns;
            TableName = tableName;
            Singular = singular;
            Columns = columns;
            Mismatches = mismatches;
            ConverterErrors = converterErrors;
        }

        public string Namespace { get; }
        public string TableName { get; }
        public string Singular { get; }
        public List<ColumnModel> Columns { get; }
        public List<string> Mismatches { get; }
        public List<(string Id, Location Location, string[] Args)> ConverterErrors { get; }
    }

    private sealed class ColumnModel
    {
        public ColumnModel(string name, string clrType, string reader, bool isIdentity, bool isRequired, string? converterFq = null)
        {
            Name = name;
            ClrType = clrType;
            Reader = reader;
            IsIdentity = isIdentity;
            IsRequired = isRequired;
            ConverterFq = converterFq;
        }

        public string Name { get; }
        public string ClrType { get; }
        public string Reader { get; }
        public bool IsIdentity { get; }
        public bool IsRequired { get; }
        public string? ConverterFq { get; }
    }
}
