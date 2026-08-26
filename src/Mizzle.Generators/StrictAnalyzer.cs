using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Mizzle.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StrictAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor NotInterceptable = new(
        "MIZ002",
        "Query is not interceptable",
        "This query terminator is not interceptable in Strict mode",
        "Mizzle",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(NotInterceptable);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            if (!IsStrict(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions))
            {
                return;
            }

            start.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
        });
    }

    private static bool IsStrict(AnalyzerConfigOptions options)
        => options.TryGetValue("build_property.MizzleQueryMode", out var mode)
           && string.Equals(mode, "Strict", StringComparison.OrdinalIgnoreCase);

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method
            || !QueryInterceptability.IsQueryTerminator(method))
        {
            return;
        }

        if (!QueryInterceptability.IsInterceptableFluentChain(invocation, context.SemanticModel))
        {
            context.ReportDiagnostic(Diagnostic.Create(NotInterceptable, invocation.GetLocation()));
        }
    }
}
