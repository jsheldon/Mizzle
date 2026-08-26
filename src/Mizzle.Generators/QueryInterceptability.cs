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
        return typeName is "Mizzle.Fluent.SelectBuilder" or "Mizzle.Fluent.UpdateBuilder"
            || Implements(method.ContainingType, "Mizzle", "IQueryExecutor");
    }

    public static bool IsInterceptableFluentChain(InvocationExpressionSyntax terminator, SemanticModel model)
    {
        if (model.GetSymbolInfo(terminator).Symbol is not IMethodSymbol method || !IsQueryTerminator(method))
        {
            return false;
        }

        if (terminator.Expression is not MemberAccessExpressionSyntax member)
        {
            return false;
        }

        var current = member.Expression;
        var sawSelect = false;
        while (current is InvocationExpressionSyntax invocation)
        {
            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol invocationMethod)
            {
                return false;
            }

            if (invocationMethod.Name == "Select")
            {
                sawSelect = true;
            }

            if (!AreVisibleArguments(invocation, model))
            {
                return false;
            }

            if (invocation.Expression is MemberAccessExpressionSyntax next)
            {
                current = next.Expression;
                continue;
            }

            return false;
        }

        return sawSelect && current is IdentifierNameSyntax;
    }

    private static bool AreVisibleArguments(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (!IsVisibleArgument(argument.Expression, model))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsVisibleArgument(ExpressionSyntax expression, SemanticModel model)
    {
        expression = expression.Trim();
        switch (expression)
        {
            case LiteralExpressionSyntax:
            case IdentifierNameSyntax:
            case DefaultExpressionSyntax:
                return true;
            case MemberAccessExpressionSyntax member
                when model.GetSymbolInfo(member).Symbol is IPropertySymbol property && IsColumnType(property.Type):
                return true;
            case InvocationExpressionSyntax invocation
                when model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "ToFrom" or "ToRef" }:
                return true;
            default:
                return false;
        }
    }

    private static bool IsColumnType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { TypeArguments.Length: 1 } named
            && named.Name is "PgColumn" or "SqlColumn")
        {
            return true;
        }

        return Implements(type, "Mizzle.Schema", "IColumn");
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

    private static ExpressionSyntax Trim(this ExpressionSyntax expression)
        => expression is ParenthesizedExpressionSyntax paren ? paren.Expression.Trim() : expression;
}
