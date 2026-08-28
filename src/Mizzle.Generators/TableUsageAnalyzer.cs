using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Mizzle.Generators;

// WithAlias builds its copy with Activator.CreateInstance, so a table with only
// a parameterized constructor throws when the query runs. The call site is
// visible at compile time, so say so then.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TableUsageAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor AliasNeedsParameterlessConstructor = new(
        "MIZ012",
        "WithAlias requires a parameterless constructor",
        "Table '{0}' has no parameterless constructor, so WithAlias cannot construct the aliased copy",
        "Mizzle",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(AliasNeedsParameterlessConstructor);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "WithAlias" }
            } invocation)
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol { Name: "WithAlias" } method)
        {
            return;
        }

        if (!IsMizzleTable(method.ContainingType) || method.ReturnType is not INamedTypeSymbol table)
        {
            return;
        }

        // Activator.CreateInstance(..., nonPublic: true) accepts a non-public one.
        if (table.InstanceConstructors.Any(c => c.Parameters.Length == 0))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            AliasNeedsParameterlessConstructor, invocation.GetLocation(), table.Name));
    }

    private static bool IsMizzleTable(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.OriginalDefinition.Name == "Table"
                && current.OriginalDefinition.ContainingNamespace?.ToDisplayString() == "Mizzle.Schema")
            {
                return true;
            }
        }

        return false;
    }
}
