using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mizzle.Generators;

// Single source of truth for the generator and the Strict analyzer: Strict polices
// exactly the terminators the projection generator can bake, and nothing else.
// Judging a shape it cannot bake -- ExecuteAsync, streaming -- would report a
// failure the caller has no way to fix.
internal static class QueryInterceptability
{
    private static readonly HashSet<string> BakeableTerminators = new(StringComparer.Ordinal)
    {
        "ToListAsync",
        "FirstAsync",
        "FirstOrDefaultAsync",
        "SingleAsync",
        "SingleOrDefaultAsync",
        "ToPageAsync",
        "ToCursorPageAsync",
    };

    public static bool IsQueryTerminator(IMethodSymbol method)
        => BakeableTerminators.Contains(method.Name)
            && method.ContainingType.ToDisplayString() == "Mizzle.Fluent.SelectBuilder";

    // A terminator is interceptable exactly when the generator can bake SQL for it.
    public static bool IsInterceptableFluentChain(InvocationExpressionSyntax terminator, SemanticModel model)
    {
        if (model.GetSymbolInfo(terminator).Symbol is not IMethodSymbol method
            || !IsQueryTerminator(method))
        {
            return false;
        }

        var spec = BakedChainWalker.TryGetSpec(terminator, model);
        return spec is not null && BakedSqlEmitter.Emit(spec) is not null;
    }
}
