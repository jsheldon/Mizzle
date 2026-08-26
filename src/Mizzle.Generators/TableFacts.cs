using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mizzle.Generators;

internal sealed class TableFactsModel
{
    public TableFactsModel(string tableName, string? schema, string alias, bool isPostgres, IReadOnlyList<TableColumnFact> columns)
    {
        TableName = tableName;
        Schema = schema;
        Alias = alias;
        IsPostgres = isPostgres;
        Columns = columns;
    }

    public string TableName { get; }
    public string? Schema { get; }
    public string Alias { get; }
    public bool IsPostgres { get; }
    public IReadOnlyList<TableColumnFact> Columns { get; }
}

internal sealed class TableColumnFact
{
    public TableColumnFact(string propertyName, string dbName, string clrTypeName)
    {
        PropertyName = propertyName;
        DbName = dbName;
        ClrTypeName = clrTypeName;
    }

    public string PropertyName { get; }
    public string DbName { get; }
    public string ClrTypeName { get; }
}

internal static class TableFacts
{
    public static TableFactsModel? FromSymbol(INamedTypeSymbol symbol)
    {
        if (symbol.IsAbstract || !IsDialectTable(symbol, out var postgres))
        {
            return null;
        }

        if (!TryCtorLiterals(symbol, out var tableName, out var schema, out var alias))
        {
            return null;
        }

        var columns = new List<TableColumnFact>();
        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsStatic || member.Type is not INamedTypeSymbol type)
            {
                continue;
            }

            if (!TryColumn(type, out var clr, out var isSqlColumn))
            {
                continue;
            }

            if (postgres == isSqlColumn)
            {
                return null;
            }

            var dbName = FactoryDbName(member);
            if (dbName is null)
            {
                return null;
            }

            columns.Add(new TableColumnFact(member.Name, dbName, ToCSharpType(clr)));
        }

        if (columns.Count == 0)
        {
            return null;
        }

        return new TableFactsModel(tableName, schema, alias ?? tableName, postgres, columns);
    }

    public static bool IsDialectTable(INamedTypeSymbol symbol, out bool postgres)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            var def = current.OriginalDefinition;
            var ns = def.ContainingNamespace.ToDisplayString();
            if (def.Name == "PgTable" && ns == "Mizzle.Postgres")
            {
                postgres = true;
                return true;
            }

            if (def.Name == "SqlTable" && ns == "Mizzle.SqlServer")
            {
                postgres = false;
                return true;
            }
        }

        postgres = false;
        return false;
    }

    public static bool TryColumn(INamedTypeSymbol type, out ITypeSymbol clr, out bool isSqlColumn)
    {
        clr = type;
        isSqlColumn = false;
        if (type.TypeArguments.Length != 1)
        {
            return false;
        }

        var ns = type.ContainingNamespace.ToDisplayString();
        if (type.Name == "PgColumn" && ns == "Mizzle.Postgres")
        {
            clr = type.TypeArguments[0];
            isSqlColumn = false;
            return true;
        }

        if (type.Name == "SqlColumn" && ns == "Mizzle.SqlServer")
        {
            clr = type.TypeArguments[0];
            isSqlColumn = true;
            return true;
        }

        return false;
    }

    public static InvocationExpressionSyntax? InnermostFactoryInvocation(IPropertySymbol member)
    {
        foreach (var syntaxRef in member.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is not PropertyDeclarationSyntax property)
            {
                continue;
            }

            if (property.Initializer?.Value is InvocationExpressionSyntax invocation)
            {
                while (invocation.Expression is MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax inner })
                {
                    invocation = inner;
                }

                return invocation;
            }
        }

        return null;
    }

    public static string? FactoryName(IPropertySymbol member)
        => InnermostFactoryInvocation(member)?.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
            _ => null
        };

    // Names of the fluent modifiers chained after the factory call, e.g.
    // Text("email").NotNull().Unique() -> ["Unique", "NotNull"].
    public static IReadOnlyList<string> ModifierNames(IPropertySymbol member)
    {
        var names = new List<string>();
        foreach (var syntaxRef in member.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is not PropertyDeclarationSyntax property
                || property.Initializer?.Value is not InvocationExpressionSyntax invocation)
            {
                continue;
            }

            while (invocation.Expression is MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax inner } access)
            {
                names.Add(access.Name.Identifier.Text);
                invocation = inner;
            }

            break;
        }

        return names;
    }

    public static string? FactoryDbName(IPropertySymbol member)
    {
        var invocation = InnermostFactoryInvocation(member);
        if (invocation is null || invocation.ArgumentList.Arguments.Count == 0)
        {
            return null;
        }

        return invocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal
               && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;
    }

    public static string ToCSharpType(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Int32 => "int",
        SpecialType.System_String => "string",
        SpecialType.System_Int64 => "long",
        SpecialType.System_Boolean => "bool",
        SpecialType.System_DateTime => "System.DateTime",
        SpecialType.System_Double => "double",
        SpecialType.System_Decimal => "decimal",
        SpecialType.System_Int16 => "short",
        SpecialType.System_Byte => "byte",
        SpecialType.System_Single => "float",
        _ => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
    };

    private static bool TryCtorLiterals(INamedTypeSymbol symbol, out string tableName, out string? schema, out string? alias)
    {
        tableName = "";
        schema = null;
        alias = null;
        foreach (var ctor in symbol.Constructors)
        {
            foreach (var syntaxRef in ctor.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is not ConstructorDeclarationSyntax { Initializer: { } initializer } declaration
                    || !initializer.IsKind(SyntaxKind.BaseConstructorInitializer))
                {
                    continue;
                }

                _ = declaration;
                var args = initializer.ArgumentList.Arguments;
                if (args.Count is < 1 or > 3)
                {
                    return false;
                }

                var values = new string?[args.Count];
                for (var i = 0; i < args.Count; i++)
                {
                    if (args[i].Expression is LiteralExpressionSyntax literal
                        && literal.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        values[i] = literal.Token.ValueText;
                        continue;
                    }

                    return false;
                }

                tableName = values[0]!;
                schema = args.Count > 1 ? values[1] : null;
                alias = args.Count > 2 ? values[2] : null;
                return true;
            }
        }

        return false;
    }
}
