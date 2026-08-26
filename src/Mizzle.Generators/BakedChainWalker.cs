using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mizzle.Generators;

// Reconstructs a BakedQuerySpec from a statically-visible fluent chain:
//   db.Select(t.Col, ...).From(t.ToFrom())[.Where(t.Col, value)][.OrderBy(t.Col.ToRef())...]
//     [.Limit(<literal>)][.Offset(<literal>)][.Distinct()].ToListAsync(map[, ct])
// Returns null for anything the generator cannot prove at compile time.
internal static class BakedChainWalker
{
    public static BakedQuerySpec? TryGetSpec(InvocationExpressionSyntax terminator, SemanticModel model)
    {
        if (terminator.Expression is not MemberAccessExpressionSyntax terminatorMember)
        {
            return null;
        }

        var calls = new List<(string Name, InvocationExpressionSyntax Invocation)>();
        var current = terminatorMember.Expression;
        while (current is InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax member)
            {
                return null;
            }

            calls.Add((member.Name.Identifier.Text, invocation));
            current = member.Expression;
        }

        var receiverType = model.GetTypeInfo(current).Type;
        var isPostgres = receiverType?.ToDisplayString() switch
        {
            "Mizzle.Postgres.PostgresDb" => true,
            "Mizzle.SqlServer.SqlDb" => false,
            _ => (bool?)null
        };
        if (isPostgres is null)
        {
            return null;
        }

        calls.Reverse();
        INamedTypeSymbol? tableType = null;
        var selectProps = new List<string>();
        var orderBy = new List<(string Prop, bool Desc)>();
        string? whereProp = null;
        int? limit = null;
        int? offset = null;
        var distinct = false;
        var sawFrom = false;

        bool TryColumnProperty(ExpressionSyntax expr, out string propertyName)
        {
            propertyName = "";
            if (expr is not MemberAccessExpressionSyntax
                || model.GetSymbolInfo(expr).Symbol is not IPropertySymbol property
                || property.Type is not INamedTypeSymbol propertyType
                || !TableFacts.TryColumn(propertyType, out _, out _))
            {
                return false;
            }

            if (tableType is null)
            {
                tableType = property.ContainingType;
            }
            else if (!SymbolEqualityComparer.Default.Equals(tableType, property.ContainingType))
            {
                return false;
            }

            propertyName = property.Name;
            return true;
        }

        for (var i = 0; i < calls.Count; i++)
        {
            var (name, invocation) = calls[i];
            var args = invocation.ArgumentList.Arguments;
            switch (name)
            {
                case "Select" when i == 0 && args.Count > 0:
                    foreach (var arg in args)
                    {
                        if (!TryColumnProperty(arg.Expression, out var prop))
                        {
                            return null;
                        }

                        selectProps.Add(prop);
                    }

                    break;
                case "From" when args.Count == 1 && !sawFrom:
                    if (args[0].Expression is not InvocationExpressionSyntax fromCall
                        || fromCall.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "ToFrom" } fromMember
                        || tableType is null
                        || !SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(fromMember.Expression).Type, tableType))
                    {
                        return null;
                    }

                    sawFrom = true;
                    break;
                case "Where" when args.Count == 2 && whereProp is null:
                    if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol whereMethod
                        || whereMethod.Parameters.Length != 2
                        || whereMethod.Parameters[0].Type.Name != "IColumn"
                        || !TryColumnProperty(args[0].Expression, out var whereColumn))
                    {
                        return null;
                    }

                    whereProp = whereColumn;
                    break;
                case "OrderBy" or "OrderByDesc" when args.Count == 1:
                    if (args[0].Expression is not InvocationExpressionSyntax toRefCall
                        || toRefCall.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "ToRef" } toRefMember
                        || !TryColumnProperty(toRefMember.Expression, out var orderColumn))
                    {
                        return null;
                    }

                    orderBy.Add((orderColumn, name == "OrderByDesc"));
                    break;
                case "Limit" when args.Count == 1 && limit is null && TryIntLiteral(args[0].Expression, out var limitValue):
                    limit = limitValue;
                    break;
                case "Offset" when args.Count == 1 && offset is null && TryIntLiteral(args[0].Expression, out var offsetValue):
                    offset = offsetValue;
                    break;
                case "Distinct" when args.Count == 0:
                    distinct = true;
                    break;
                default:
                    return null;
            }
        }

        if (tableType is null || selectProps.Count == 0 || !sawFrom)
        {
            return null;
        }

        var facts = TableFacts.FromSymbol(tableType);
        if (facts is null || facts.IsPostgres != isPostgres.Value)
        {
            return null;
        }

        var dbNames = new Dictionary<string, string>();
        foreach (var column in facts.Columns)
        {
            dbNames[column.PropertyName] = column.DbName;
        }

        var selectDbNames = new List<string>();
        foreach (var prop in selectProps)
        {
            if (!dbNames.TryGetValue(prop, out var dbName))
            {
                return null;
            }

            selectDbNames.Add(dbName);
        }

        string? whereDbName = null;
        if (whereProp is not null && !dbNames.TryGetValue(whereProp, out whereDbName))
        {
            return null;
        }

        var orderByDb = new List<(string DbName, bool Desc)>();
        foreach (var (prop, desc) in orderBy)
        {
            if (!dbNames.TryGetValue(prop, out var dbName))
            {
                return null;
            }

            orderByDb.Add((dbName, desc));
        }

        return new BakedQuerySpec(
            isPostgres.Value,
            facts,
            selectDbNames,
            distinct,
            whereDbName,
            orderByDb,
            limit,
            offset);
    }

    private static bool TryIntLiteral(ExpressionSyntax expression, out int value)
    {
        value = 0;
        if (expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.NumericLiteralExpression)
            && literal.Token.Value is int intValue)
        {
            value = intValue;
            return true;
        }

        return false;
    }
}
