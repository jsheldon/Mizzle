using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Mizzle.Generators;

// AlwaysFilter columns must appear in WHERE on every statically visible
// select, update, or delete against their table. JOIN ON and WhereIf do not
// count: the filter can still be omitted at runtime.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AlwaysFilterAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor MissingFilter = new(
        "MIZ013",
        "AlwaysFilter column is missing from WHERE",
        "Column '{0}' on table '{1}' is marked AlwaysFilter but is not constrained in WHERE",
        "Mizzle",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(MissingFilter);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        var query = BakedChainWalker.TryGetAlwaysFilterQuery(invocation, context.SemanticModel);
        if (query is null)
        {
            return;
        }

        // Every diagnostic lands on the terminator, so the same column missed in
        // several scopes -- five union branches over one table, say -- would read
        // as five identical warnings on one line. Report each column once.
        ReportMissing(context, invocation, query, []);
    }

    private static void ReportMissing(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        AlwaysFilterQuery query,
        HashSet<(string Table, string Property)> reported)
    {
        var filtered = new HashSet<(string Alias, string DbName)>();
        foreach (var condition in query.Where)
        {
            filtered.Add((condition.LeftAlias, condition.LeftDbName));
            if (condition.RightAlias is not null && condition.RightDbName is not null)
            {
                filtered.Add((condition.RightAlias, condition.RightDbName));
            }
        }

        foreach (var table in query.Tables)
        {
            foreach (var column in table.Facts.Columns)
            {
                if (column.IsAlwaysFilter
                    && !filtered.Contains((table.Alias, column.DbName))
                    && reported.Add((table.Facts.TableName, column.PropertyName)))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MissingFilter,
                        invocation.GetLocation(),
                        column.PropertyName,
                        table.Facts.TableName));
                }
            }
        }

        foreach (var nested in query.Nested)
        {
            ReportMissing(context, invocation, nested, reported);
        }
    }
}
