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

    // A terminator is interceptable when the generator can bake SQL for it, OR
    // when it resolves via the dynamic-mapper fallback -- a bound T whose select
    // shape survives every reassignment gets a real generated mapper forwarded to
    // a delegate-based overload (see ProjectionGenerator.Transform). Judging only
    // full baking here would report MIZ002 for a call site the generator itself
    // already made safe.
    public static bool IsInterceptableFluentChain(InvocationExpressionSyntax terminator, SemanticModel model)
    {
        if (model.GetSymbolInfo(terminator).Symbol is not IMethodSymbol method
            || !IsQueryTerminator(method))
        {
            return false;
        }

        var spec = BakedChainWalker.TryGetSpec(terminator, model);
        if (spec is not null && BakedSqlEmitter.Emit(spec) is not null)
        {
            return true;
        }

        return IsResolvableByDynamicMapper(terminator, model);
    }

    private static bool IsResolvableByDynamicMapper(InvocationExpressionSyntax terminator, SemanticModel model)
    {
        if (terminator.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } member
            || generic.TypeArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        var argType = model.GetTypeInfo(generic.TypeArgumentList.Arguments[0]).Type;
        if (argType is not INamedTypeSymbol bound || argType is IErrorTypeSymbol)
        {
            return false;
        }

        var dynamicSelect = BakedChainWalker.TryGetProjectionOnlySelect(member.Expression, model);
        return dynamicSelect is not null
            && ProjectionGenerator.CanBuildMapPlan(bound, dynamicSelect, model.Compilation);
    }
}
