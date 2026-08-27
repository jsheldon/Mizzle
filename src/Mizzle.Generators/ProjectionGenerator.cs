using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Mizzle.Generators;

// Handles the delegate-free typed terminators (ToListAsync<T>() and friends).
// T unbound  -> generate a projection record named T from the select shape.
// T bound    -> map into the existing type, matching members by normalized name.
// Either way an interceptor bakes SQL + mapper through the precompiled path.
[Generator]
public sealed class ProjectionGenerator : IIncrementalGenerator
{
    internal static readonly DiagnosticDescriptor NotVisible = new(
        "MIZ007",
        "Cannot generate projection type",
        "Cannot generate projection type '{0}': the query shape is not statically visible",
        "Mizzle",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor NoTargetMember = new(
        "MIZ003",
        "Selected column has no matching member",
        "Selected column '{0}' has no matching member on projection target '{1}'",
        "Mizzle",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor RequiredMemberUnfilled = new(
        "MIZ004",
        "Required member has no matching column",
        "Required member '{0}' of projection target '{1}' has no matching selected column",
        "Mizzle",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor NullableIntoNonNullable = new(
        "MIZ005",
        "Nullable column mapped to non-nullable member",
        "Column '{0}' can be NULL (schema or LEFT JOIN) but member '{1}' of '{2}' is non-nullable",
        "Mizzle",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor AmbiguousMember = new(
        "MIZ006",
        "Ambiguous projection member match",
        "Column '{0}' matches more than one member of projection target '{1}'",
        "Mizzle",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MemberTypeMismatch = new(
        "MIZ010",
        "Selected column type does not match member type",
        "Column '{0}' reads as '{1}' but member '{2}' of '{3}' is '{4}'",
        "Mizzle",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly Dictionary<string, DiagnosticDescriptor> Descriptors = new()
    {
        ["MIZ010"] = MemberTypeMismatch,
        ["MIZ003"] = NoTargetMember,
        ["MIZ004"] = RequiredMemberUnfilled,
        ["MIZ005"] = NullableIntoNonNullable,
        ["MIZ006"] = AmbiguousMember,
        ["MIZ007"] = NotVisible,
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var sites = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name: GenericNameSyntax }
                },
                static (ctx, _) => Transform(ctx))
            .Where(static site => site is not null)
            .Select(static (site, _) => site!)
            .Collect();

        var trimStrings = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
                provider.GlobalOptions.TryGetValue("build_property.MizzleTrimStrings", out var value)
                && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

        context.RegisterSourceOutput(
            sites.Combine(trimStrings),
            static (spc, pair) => Generate(spc, pair.Left, pair.Right));
    }

    private static ProjectionSite? Transform(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var member = (MemberAccessExpressionSyntax)invocation.Expression;
        var generic = (GenericNameSyntax)member.Name;
        var terminator = generic.Identifier.Text switch
        {
            "ToListAsync" => "ToList",
            "FirstAsync" => "First",
            "FirstOrDefaultAsync" => "FirstOrDefault",
            "SingleAsync" => "Single",
            "SingleOrDefaultAsync" => "SingleOrDefault",
            _ => null
        };
        if (terminator is null
            || generic.TypeArgumentList.Arguments.Count != 1
            || invocation.ArgumentList.Arguments.Count > 1)
        {
            return null;
        }

        var model = context.SemanticModel;
        var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol
            ?? model.GetSymbolInfo(invocation).CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (symbol is null
            || symbol.Name != generic.Identifier.Text
            || symbol.ContainingType.ToDisplayString() != "Mizzle.Fluent.SelectBuilder"
            || symbol.Parameters.Length != 1)
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
        var bound = argType is INamedTypeSymbol named && argType is not IErrorTypeSymbol ? (INamedTypeSymbol?)named : null;

        var ns = invocation.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault()?.Name.ToString() ?? "";

        var spec = BakedChainWalker.TryGetSpec(invocation, model, out var hasReportedColumnError);
        var sql = spec is null ? null : BakedSqlEmitter.Emit(spec);
        if (spec is null || sql is null)
        {
            // Only unbound T deserves MIZ007 — a bound T on a dynamic chain
            // simply falls back to the runtime stub (and MIZ002 under Strict).
            // A table whose column already reported MIZ008/MIZ009 is silent
            // either way: that diagnostic points at the real line.
            return bound is null && !hasReportedColumnError
                ? new ProjectionSite(typeName, ns, terminator, null, null, null, null, [("MIZ007", [typeName])], invocation.GetLocation())
                : null;
        }

        MapperPlan? mapperPlan = null;
        var errors = new List<(string Id, string[] Args)>();
        if (bound is not null)
        {
            mapperPlan = BuildMapPlan(bound, spec, model.Compilation, errors);
        }

#pragma warning disable RSEXPERIMENTAL002
        var location = model.GetInterceptableLocation(invocation);
        if (location is null)
        {
            return null;
        }

        var attribute = location.GetInterceptsLocationAttributeSyntax();
#pragma warning restore RSEXPERIMENTAL002
        return new ProjectionSite(typeName, ns, terminator, spec, sql, attribute, mapperPlan, errors, invocation.GetLocation());
    }

    // Records which selected column ordinal fills which member, or records
    // diagnostics into errors and returns null. Expression text is composed later,
    // in Generate, because it depends on the MizzleTrimStrings build property,
    // which only reaches the pipeline at the RegisterSourceOutput stage.
    private static MapperPlan? BuildMapPlan(
        INamedTypeSymbol target,
        BakedQuerySpec spec,
        Compilation compilation,
        List<(string Id, string[] Args)> errors)
    {
        static string Norm(string name) => name.Replace("_", "").ToLowerInvariant();

        var targetName = target.Name;
        var ctor = target.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .Where(c => !(c.Parameters.Length == 1
                && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, target)))
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();
        var properties = target.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && !p.IsIndexer && p.SetMethod is not null
                && p.DeclaredAccessibility == Accessibility.Public)
            // A record's positional parameters synthesize same-named properties;
            // the constructor parameter is the real target.
            .Where(p => ctor is null || ctor.Parameters.All(cp => cp.Name != p.Name))
            .ToList();

        var usedParams = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
        var usedProps = new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default);
        var ctorArgs = new Dictionary<string, int>();
        var propAssigns = new List<(string Name, int Ordinal)>();

        for (var i = 0; i < spec.Select.Count; i++)
        {
            var column = spec.Select[i];
            var norm = Norm(column.MemberName);
            var paramMatches = ctor is null
                ? []
                : ctor.Parameters.Where(p => !usedParams.Contains(p) && Norm(p.Name) == norm).ToList();
            var propMatches = properties.Where(p => !usedProps.Contains(p) && Norm(p.Name) == norm).ToList();
            var total = paramMatches.Count + propMatches.Count;
            if (total == 0)
            {
                errors.Add(("MIZ003", [column.MemberName, targetName]));
                continue;
            }

            if (total > 1)
            {
                errors.Add(("MIZ006", [column.MemberName, targetName]));
                continue;
            }

            var memberType = paramMatches.Count == 1 ? paramMatches[0].Type : propMatches[0].Type;
            var memberName = paramMatches.Count == 1 ? paramMatches[0].Name : propMatches[0].Name;
            if (!column.IsRequired && !IsNullable(memberType))
            {
                errors.Add(("MIZ005", [column.MemberName, memberName, targetName]));
                continue;
            }

            // Nullability is settled above; compare the underlying types so a
            // converted column landing on the wrong member is caught here rather
            // than as CS0029 inside the generated mapper.
            var underlying = memberType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? ((INamedTypeSymbol)memberType).TypeArguments[0]
                : memberType;
            var columnType = TableFacts.ResolveClrType(column.ClrTypeName, compilation);
            if (columnType is not null && !compilation.ClassifyConversion(columnType, underlying).IsImplicit)
            {
                errors.Add(("MIZ010", [
                    column.MemberName,
                    column.ClrTypeName,
                    memberName,
                    targetName,
                    memberType.ToDisplayString()
                ]));
                continue;
            }

            if (paramMatches.Count == 1)
            {
                usedParams.Add(paramMatches[0]);
                ctorArgs[paramMatches[0].Name] = i;
            }
            else
            {
                usedProps.Add(propMatches[0]);
                propAssigns.Add((propMatches[0].Name, i));
            }
        }

        if (ctor is not null)
        {
            foreach (var parameter in ctor.Parameters.Where(p => !usedParams.Contains(p) && !p.HasExplicitDefaultValue))
            {
                errors.Add(("MIZ004", [parameter.Name, targetName]));
            }
        }

        foreach (var property in properties.Where(p => !usedProps.Contains(p) && !IsNullable(p.Type)))
        {
            errors.Add(("MIZ004", [property.Name, targetName]));
        }

        if (errors.Count > 0)
        {
            return null;
        }

        var fq = target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var orderedCtorArgs = ctor is null
            ? []
            : ctor.Parameters.Where(usedParams.Contains).Select(p => (p.Name, ctorArgs[p.Name])).ToList();
        return new MapperPlan(fq, orderedCtorArgs, propAssigns);
    }

    // Composes "new global::Ns.T(param: expr, ...) { Prop = expr, ... }" once the
    // trim flag is known.
    private static string MapperBody(MapperPlan plan, BakedQuerySpec spec, bool trimStrings)
    {
        var args = string.Join(
            ", ",
            plan.CtorArgs.Select(a => $"{a.Name}: {ReadExpression(spec.Select[a.Ordinal], a.Ordinal, trimStrings)}"));
        var body = $"new {plan.TargetFq}({args})";
        if (plan.PropAssigns.Count > 0)
        {
            body += " { " + string.Join(
                ", ",
                plan.PropAssigns.Select(a => $"{a.Name} = {ReadExpression(spec.Select[a.Ordinal], a.Ordinal, trimStrings)}")) + " }";
        }

        return body;
    }

    private sealed class MapperPlan
    {
        public MapperPlan(string targetFq, List<(string Name, int Ordinal)> ctorArgs, List<(string Name, int Ordinal)> propAssigns)
        {
            TargetFq = targetFq;
            CtorArgs = ctorArgs;
            PropAssigns = propAssigns;
        }

        public string TargetFq { get; }
        public List<(string Name, int Ordinal)> CtorArgs { get; }
        public List<(string Name, int Ordinal)> PropAssigns { get; }
    }

    private static bool IsNullable(ITypeSymbol type)
        => type.NullableAnnotation == NullableAnnotation.Annotated
            || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static void Generate(SourceProductionContext context, ImmutableArray<ProjectionSite> sites, bool trimStrings)
    {
        if (sites.Length == 0)
        {
            return;
        }

        foreach (var site in sites)
        {
            foreach (var (id, args) in site.Errors)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors[id], site.Location, [..args.Cast<object?>()]));
            }
        }

        var valid = sites.Where(s => s.Sql is not null && s.Errors.Count == 0).ToList();
        if (valid.Count == 0)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        AppendInterceptsLocationDeclaration(sb);

        // Generate mode: one record + mapper per distinct (namespace, name); first shape wins.
        var generated = valid.Where(s => s.MapperPlan is null).GroupBy(s => (s.Namespace, s.TypeName)).ToList();
        foreach (var group in generated)
        {
            var spec = group.First().Spec!;
            var hasNamespace = group.Key.Namespace.Length > 0;
            if (hasNamespace)
            {
                sb.Append("namespace ");
                sb.AppendLine(group.Key.Namespace);
                sb.AppendLine("{");
            }

            sb.Append(hasNamespace ? "    " : "");
            sb.Append("public sealed record ");
            sb.Append(group.Key.TypeName);
            sb.Append('(');
            sb.Append(string.Join(", ", spec.Select.Select(MemberType)));
            sb.AppendLine(");");
            if (hasNamespace)
            {
                sb.AppendLine("}");
            }
        }

        sb.AppendLine("namespace Mizzle.Generated.Projections");
        sb.AppendLine("{");
        foreach (var group in generated)
        {
            var spec = group.First().Spec!;
            var fq = FullyQualified(group.Key.Namespace, group.Key.TypeName);
            EmitMapper(
                sb,
                GeneratedMapperName(group.Key.TypeName),
                fq,
                $"new {fq}({string.Join(", ", spec.Select.Select((c, i) => ReadCall(c, i, trimStrings)))})");
        }

        // Map mode: one mapper per distinct (namespace, type, sql, terminator).
        var mapped = valid.Where(s => s.MapperPlan is not null)
            .GroupBy(s => (s.Namespace, s.TypeName, s.Sql, s.Terminator))
            .Select((g, i) => (Group: g, Name: $"{g.Key.TypeName}IntoMapper{i}"))
            .ToList();
        foreach (var (group, name) in mapped)
        {
            var first = group.First();
            // The target's real namespace, not the call site's -- a bound T is
            // routinely declared in another assembly or layer.
            EmitMapper(sb, name, first.MapperPlan!.TargetFq, MapperBody(first.MapperPlan!, first.Spec!, trimStrings));
        }

        sb.AppendLine("}");

        sb.AppendLine("namespace Mizzle.Generated.Interceptors");
        sb.AppendLine("{");
        sb.AppendLine("    public static class MizzleProjections");
        sb.AppendLine("    {");
        var interceptorGroups = generated
            .SelectMany(g => g.GroupBy(s => (s.Sql, s.Terminator)).Select(bySql =>
                (Sites: bySql.AsEnumerable(), Mapper: GeneratedMapperName(g.Key.TypeName), Sql: bySql.Key.Sql!, Terminator: bySql.Key.Terminator)))
            .Concat(mapped.Select(m => (Sites: m.Group.AsEnumerable(), Mapper: m.Name, Sql: m.Group.Key.Sql!, Terminator: m.Group.Key.Terminator)))
            .ToList();
        for (var i = 0; i < interceptorGroups.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
            }

            EmitInterceptor(sb, i, interceptorGroups[i].Terminator, interceptorGroups[i].Mapper, interceptorGroups[i].Sql, interceptorGroups[i].Sites.Select(s => s.Attribute!).Distinct());
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("Mizzle.Projections.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void EmitInterceptor(StringBuilder sb, int index, string terminator, string mapper, string sql, IEnumerable<string> attributes)
    {
        const string listOfT = "global::System.Collections.Generic.IReadOnlyList<T>";
        var returnType = terminator switch
        {
            "ToList" => listOfT,
            "First" or "Single" => "T",
            _ => "T?"
        };
        foreach (var attribute in attributes)
        {
            sb.Append("        ");
            sb.AppendLine(attribute);
        }

        sb.Append("        public static async global::System.Threading.Tasks.Task<");
        sb.Append(returnType);
        sb.Append("> ");
        sb.Append(terminator);
        sb.Append("Projected");
        sb.Append(index);
        sb.AppendLine("<T>(");
        sb.AppendLine("            this global::Mizzle.Fluent.SelectBuilder builder,");
        sb.AppendLine("            global::System.Threading.CancellationToken cancellationToken = default)");
        sb.AppendLine("        {");
        sb.Append("            var rows = await builder.ToListPrecompiledAsync(");
        sb.Append(SymbolDisplay.FormatLiteral(sql, quote: true));
        sb.Append(", global::Mizzle.Generated.Projections.");
        sb.Append(mapper);
        sb.AppendLine(".Read, cancellationToken);");
        switch (terminator)
        {
            case "ToList":
                sb.AppendLine("            return (global::System.Collections.Generic.IReadOnlyList<T>)(object)rows;");
                break;
            case "First":
                sb.AppendLine("            if (rows.Count == 0) throw new global::System.InvalidOperationException(\"Sequence contains no elements.\");");
                sb.AppendLine("            return (T)(object)rows[0]!;");
                break;
            case "FirstOrDefault":
                sb.AppendLine("            return rows.Count == 0 ? default : (T?)(object?)rows[0];");
                break;
            case "Single":
                sb.AppendLine("            if (rows.Count == 0) throw new global::System.InvalidOperationException(\"Sequence contains no elements.\");");
                sb.AppendLine("            if (rows.Count > 1) throw new global::System.InvalidOperationException(\"Sequence contains more than one element.\");");
                sb.AppendLine("            return (T)(object)rows[0]!;");
                break;
            default:
                sb.AppendLine("            if (rows.Count > 1) throw new global::System.InvalidOperationException(\"Sequence contains more than one element.\");");
                sb.AppendLine("            return rows.Count == 0 ? default : (T?)(object?)rows[0];");
                break;
        }

        sb.AppendLine("        }");
    }

    private static void EmitMapper(StringBuilder sb, string mapperName, string resultType, string body)
    {
        sb.Append("    public static class ");
        sb.AppendLine(mapperName);
        sb.AppendLine("    {");
        sb.Append("        public static ");
        sb.Append(resultType);
        sb.AppendLine(" Read(global::System.Data.Common.DbDataReader r)");
        sb.Append("            => ");
        sb.Append(body);
        sb.AppendLine(";");
        sb.AppendLine("    }");
    }

    // File-local declaration so generated interceptors compile even when the
    // referenced BCL does not expose InterceptsLocationAttribute publicly.
    internal static void AppendInterceptsLocationDeclaration(StringBuilder sb)
    {
        sb.AppendLine("namespace System.Runtime.CompilerServices");
        sb.AppendLine("{");
        sb.AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]");
        sb.AppendLine("    file sealed class InterceptsLocationAttribute : global::System.Attribute");
        sb.AppendLine("    {");
        sb.AppendLine("        public InterceptsLocationAttribute(int version, string data)");
        sb.AppendLine("        {");
        sb.AppendLine("            _ = version;");
        sb.AppendLine("            _ = data;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    private static string MemberType(BakedColumn column)
        => column.IsRequired
            ? $"{column.ClrTypeName} {column.MemberName}"
            : $"{column.ClrTypeName}? {column.MemberName}";

    private static string ReadCall(BakedColumn column, int ordinal, bool trimStrings)
        => ReadExpression(column, ordinal, trimStrings);

    private static string ReadExpression(BakedColumn column, int ordinal, bool trimStrings)
    {
        var storage = $"r.{column.ReaderCall}({ordinal})";
        if (trimStrings && !column.IsUntrimmed && column.ReaderCall == "GetString")
        {
            storage += ".Trim()";
        }

        var read = column.ReadConverter is null ? storage : $"{column.ReadConverter}({storage})";
        return column.IsRequired
            ? read
            : $"r.IsDBNull({ordinal}) ? ({column.ClrTypeName}?)null : {read}";
    }

    private static string GeneratedMapperName(string typeName) => typeName + "ProjectionMapper";

    private static string FullyQualified(string ns, string name)
        => ns.Length == 0 ? "global::" + name : $"global::{ns}.{name}";

    private sealed class ProjectionSite
    {
        public ProjectionSite(
            string typeName,
            string ns,
            string terminator,
            BakedQuerySpec? spec,
            string? sql,
            string? attribute,
            MapperPlan? mapperPlan,
            List<(string Id, string[] Args)> errors,
            Location location)
        {
            TypeName = typeName;
            Namespace = ns;
            Terminator = terminator;
            Spec = spec;
            Sql = sql;
            Attribute = attribute;
            MapperPlan = mapperPlan;
            Errors = errors;
            Location = location;
        }

        public string TypeName { get; }
        public string Namespace { get; }
        public string Terminator { get; }
        public BakedQuerySpec? Spec { get; }
        public string? Sql { get; }
        public string? Attribute { get; }
        public MapperPlan? MapperPlan { get; }
        public List<(string Id, string[] Args)> Errors { get; }
        public Location Location { get; }
    }
}
