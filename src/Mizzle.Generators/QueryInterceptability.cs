using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mizzle.Generators;

internal static class QueryInterceptability
{
    public static bool IsQueryTerminator(IMethodSymbol method)
    {
        if (method.Name is not (
            "ToListAsync" or
            "FirstAsync" or
            "SingleAsync" or
            "ToAsyncEnumerable" or
            "ToPageAsync" or
            "ToCursorPageAsync" or
            "ExecuteAsync"))
        {
            return false;
        }

        var typeName = method.ContainingType.ToDisplayString();
        return typeName is "Mizzle.Fluent.SelectBuilder"
            or "Mizzle.Fluent.InsertBuilder"
            or "Mizzle.Fluent.UpdateBuilder"
            or "Mizzle.Fluent.DeleteBuilder"
            || Implements(method.ContainingType, "Mizzle", "IQueryExecutor");
    }

    // Single source of truth for generator and analyzer: a terminator is
    // interceptable exactly when the generator can bake SQL for it.
    public static bool IsInterceptableFluentChain(InvocationExpressionSyntax terminator, SemanticModel model)
    {
        if (model.GetSymbolInfo(terminator).Symbol is not IMethodSymbol { Name: "ToListAsync" } method
            || method.ContainingType.ToDisplayString() != "Mizzle.Fluent.SelectBuilder")
        {
            return false;
        }

        var spec = BakedChainWalker.TryGetSpec(terminator, model);
        return spec is not null && BakedSqlEmitter.Emit(spec) is not null;
    }

    private static bool Implements(ITypeSymbol type, string ns, string name)
    {
        if (type.Name == name && type.ContainingNamespace.ToDisplayString() == ns)
        {
            return true;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.Name == name && iface.ContainingNamespace.ToDisplayString() == ns)
            {
                return true;
            }
        }

        return false;
    }
}
