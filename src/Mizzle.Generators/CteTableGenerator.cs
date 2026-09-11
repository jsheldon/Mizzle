using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Mizzle.Generators;

// CteBuilder.Named<T>(name, body): declares T as a table type with one typed
// column per column the body's select list projects, so a CTE never needs a
// hand-declared table whose columns must be kept in sync with the body by hand.
// Mirrors ProjectionGenerator's Generate mode, but the generated members are
// real IColumn-typed properties (usable in Select/Where/From/joins), not plain
// data properties.
[Generator]
public sealed class CteTableGenerator : IIncrementalGenerator
{
    internal static readonly DiagnosticDescriptor NotStaticallyVisible = new(
        "MIZ015",
        "Cannot generate CTE table",
        "Cannot generate CTE table '{0}': the CTE name or body is not statically visible",
        "Mizzle",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UnsupportedColumnType = new(
        "MIZ016",
        "Cannot generate a CTE table column for this type",
        "Column '{0}' on CTE table '{1}' has type '{2}', which has no known column factory; "
        + "scaffold this CTE's table by hand instead",
        "Mizzle",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly Dictionary<string, DiagnosticDescriptor> Descriptors = new()
    {
        ["MIZ015"] = NotStaticallyVisible,
        ["MIZ016"] = UnsupportedColumnType,
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var sites = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.Text: "Named" } }
                },
                static (ctx, _) => Transform(ctx))
            .Where(static site => site is not null)
            .Select(static (site, _) => site!)
            .Collect();

        context.RegisterSourceOutput(sites, static (spc, sites) => Generate(spc, sites));
    }

    private static CteTableSite? Transform(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var member = (MemberAccessExpressionSyntax)invocation.Expression;
        var generic = (GenericNameSyntax)member.Name;
        var model = context.SemanticModel;

        if (generic.TypeArgumentList.Arguments.Count != 1
            || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol
            {
                ContainingType.Name: "CteBuilder",
                ContainingType.ContainingNamespace: { } ns
            } symbol
            || ns.ToDisplayString() != "Mizzle.Fluent"
            || symbol.Parameters.Length != 2)
        {
            return null;
        }

        var typeArg = generic.TypeArgumentList.Arguments[0];
        var typeName = typeArg.ToString();
        if (typeName.Contains('.'))
        {
            return null;
        }

        var argType = model.GetTypeInfo(typeArg).Type;
        if (argType is INamedTypeSymbol && argType is not IErrorTypeSymbol)
        {
            // T already exists -- nothing to generate. A real conflicting type is
            // the caller's problem to resolve, same as Generate mode for records.
            return null;
        }

        var declarationNamespace = invocation.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault()?.Name.ToString() ?? "";

        var cte = BakedChainWalker.TryGetCte(invocation, model);
        if (cte is null)
        {
            return new CteTableSite(typeName, declarationNamespace, null, [("MIZ015", [typeName])], invocation.GetLocation());
        }

        var columns = new List<CteTableColumn>();
        var errors = new List<(string Id, string[] Args)>();
        foreach (var column in cte.Body.Select)
        {
            var factory = ResolveFactory(column.ClrTypeName, cte.Body.IsPostgres);
            if (factory is null)
            {
                errors.Add(("MIZ016", [column.MemberName, typeName, column.ClrTypeName]));
                continue;
            }

            // The property name (MemberName) is a C# identifier -- it can differ
            // from the CTE's actual result-set column name, which is the db
            // column name for an unaliased passthrough, or the alias for a
            // computed expression (which has no db column at all).
            var sqlName = column.ProjectionName ?? column.DbName;
            columns.Add(new CteTableColumn(column.MemberName, sqlName, column.ClrTypeName, factory, column.IsRequired));
        }

        if (errors.Count > 0)
        {
            return new CteTableSite(typeName, declarationNamespace, null, errors, invocation.GetLocation());
        }

        var shape = new CteTableShape(cte.Name, cte.Body.IsPostgres, columns);
        return new CteTableSite(typeName, declarationNamespace, shape, [], invocation.GetLocation());
    }

    // Only types with an unambiguous, length-free factory: a CTE's projected
    // column has no natural length the way a real table column does.
    private static string? ResolveFactory(string clrTypeName, bool isPostgres)
    {
        var dot = clrTypeName.LastIndexOf('.');
        var simple = dot < 0 ? clrTypeName : clrTypeName.Substring(dot + 1);
        return (simple, isPostgres) switch
        {
            ("string", _) => "Text",
            ("int", true) => "Integer",
            ("int", false) => "Int",
            ("long", _) => "BigInt",
            ("short", _) => "SmallInt",
            ("bool", true) => "Boolean",
            ("bool", false) => "Bit",
            ("decimal", true) => "Numeric",
            ("decimal", false) => "Decimal",
            ("double", true) => "DoublePrecision",
            ("double", false) => "Float",
            ("float", _) => "Real",
            ("Guid", true) => "Uuid",
            ("Guid", false) => "UniqueIdentifier",
            ("DateOnly", _) => "Date",
            ("DateTime", true) => "Timestamp",
            ("DateTime", false) => "DateTime2",
            ("DateTimeOffset", true) => "Timestamptz",
            _ => null
        };
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<CteTableSite> sites)
    {
        foreach (var site in sites)
        {
            foreach (var (id, args) in site.Errors)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors[id], site.Location, [.. args.Cast<object?>()]));
            }
        }

        var valid = sites.Where(s => s.Shape is not null && s.Errors.Count == 0)
            .GroupBy(s => (s.Namespace, s.TypeName))
            .ToList();
        if (valid.Count == 0)
        {
            return;
        }

        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine("// <auto-generated />");
        stringBuilder.AppendLine("#nullable enable");
        foreach (var group in valid)
        {
            var shape = group.First().Shape!;
            var baseType = shape.IsPostgres ? "global::Mizzle.Postgres.PgTable" : "global::Mizzle.SqlServer.SqlTable";
            var columnType = shape.IsPostgres ? "global::Mizzle.Postgres.PgColumn" : "global::Mizzle.SqlServer.SqlColumn";

            var hasNamespace = group.Key.Namespace.Length > 0;
            if (hasNamespace)
            {
                stringBuilder.Append("namespace ").AppendLine(group.Key.Namespace);
                stringBuilder.AppendLine("{");
            }

            var indent = hasNamespace ? "    " : "";
            stringBuilder.Append(indent).Append("public sealed class ").Append(group.Key.TypeName)
                .Append(" : ").Append(baseType).Append('<').Append(group.Key.TypeName).AppendLine(">");
            stringBuilder.Append(indent).AppendLine("{");
            stringBuilder.Append(indent).Append("    public ").Append(group.Key.TypeName).Append("() : base(")
                .Append(SymbolDisplay.FormatLiteral(shape.CteName, quote: true)).AppendLine(") { }");
            stringBuilder.AppendLine();
            foreach (var column in shape.Columns)
            {
                stringBuilder.Append(indent).Append("    public ").Append(columnType).Append('<').Append(column.ClrTypeName).Append("> ")
                    .Append(column.Name).Append(" { get; } = ").Append(column.Factory).Append('(')
                    .Append(SymbolDisplay.FormatLiteral(column.SqlName, quote: true)).Append(')')
                    .Append(column.Required ? ".NotNull()" : "").AppendLine(";");
            }

            stringBuilder.Append(indent).AppendLine("}");
            if (hasNamespace)
            {
                stringBuilder.AppendLine("}");
            }
        }

        context.AddSource("Mizzle.CteTables.g.cs", SourceText.From(stringBuilder.ToString(), Encoding.UTF8));
    }

    private sealed class CteTableColumn
    {
        public CteTableColumn(string name, string sqlName, string clrTypeName, string factory, bool required)
        {
            Name = name;
            SqlName = sqlName;
            ClrTypeName = clrTypeName;
            Factory = factory;
            Required = required;
        }

        // The C# property name.
        public string Name { get; }

        // The CTE's actual result-set column name, passed to the factory call.
        public string SqlName { get; }

        public string ClrTypeName { get; }
        public string Factory { get; }
        public bool Required { get; }
    }

    private sealed class CteTableShape
    {
        public CteTableShape(string cteName, bool isPostgres, IReadOnlyList<CteTableColumn> columns)
        {
            CteName = cteName;
            IsPostgres = isPostgres;
            Columns = columns;
        }

        public string CteName { get; }
        public bool IsPostgres { get; }
        public IReadOnlyList<CteTableColumn> Columns { get; }
    }

    private sealed class CteTableSite
    {
        public CteTableSite(
            string typeName,
            string ns,
            CteTableShape? shape,
            List<(string Id, string[] Args)> errors,
            Location location)
        {
            TypeName = typeName;
            Namespace = ns;
            Shape = shape;
            Errors = errors;
            Location = location;
        }

        public string TypeName { get; }
        public string Namespace { get; }
        public CteTableShape? Shape { get; }
        public List<(string Id, string[] Args)> Errors { get; }
        public Location Location { get; }
    }
}
