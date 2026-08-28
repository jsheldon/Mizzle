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

internal enum MapStatus
{
    None,
    Valid,
    Invalid,

    // Map<TResult> produced a Nullable<T> column type. Column T is the storage
    // type; nullability belongs to IsRequired, so the two would collide and the
    // generators would emit "Guid??".
    NullableResult,
}

internal sealed class TableColumnFact
{
    public TableColumnFact(string propertyName, string dbName, string clrTypeName, bool isRequired, string readerCall, string? readConverter = null, bool isUntrimmed = false)
    {
        PropertyName = propertyName;
        DbName = dbName;
        ClrTypeName = clrTypeName;
        IsRequired = isRequired;
        ReaderCall = readerCall;
        ReadConverter = readConverter;
        IsUntrimmed = isUntrimmed;
    }

    // Fully-qualified static method wrapped around the storage read, from .Map(read, write).
    public string? ReadConverter { get; }

    public string PropertyName { get; }
    public string DbName { get; }
    public string ClrTypeName { get; }

    // NotNull()/PrimaryKey() modifier or Identity factory. Ignores join
    // semantics — a LeftJoin target's columns are nullable regardless.
    public bool IsRequired { get; }

    // DbDataReader accessor: GetString / GetGuid / GetFieldValue<...>
    public string ReaderCall { get; }

    // Untrimmed() modifier: excludes this column from MizzleTrimStrings.
    public bool IsUntrimmed { get; }
}

internal static class TableFacts
{
    public static TableFactsModel? FromSymbol(INamedTypeSymbol symbol, Compilation compilation)
    {
        if (symbol.IsAbstract || !IsDialectTable(symbol, out var postgres))
        {
            return null;
        }

        if (!TryCtorLiterals(symbol, out var tableName, out var schema))
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

            var modifiers = ModifierNames(member);
            var isRequired = FactoryName(member) == "Identity"
                || modifiers.Contains("NotNull")
                || modifiers.Contains("PrimaryKey");
            var isUntrimmed = modifiers.Contains("Untrimmed");
            var mapStatus = GetMapInfo(member, compilation, out var converterFq, out var storageReader, out _);
            if (mapStatus is MapStatus.Invalid or MapStatus.NullableResult)
            {
                return null;
            }

            columns.Add(mapStatus == MapStatus.Valid
                ? new TableColumnFact(member.Name, dbName, ToCSharpType(clr), isRequired, storageReader!, converterFq, isUntrimmed)
                : new TableColumnFact(member.Name, dbName, ToCSharpType(clr), isRequired, ReaderCall(clr), null, isUntrimmed));
        }

        if (columns.Count == 0)
        {
            return null;
        }

        return new TableFactsModel(tableName, schema, tableName, postgres, columns);
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

    // Analyzes a column property's initializer chain for .Map(read, write).
    // Valid: read argument is a static method reference -> converterFq + the
    // factory's storage reader. Invalid: Map present but not bakeable.
    public static MapStatus GetMapInfo(
        IPropertySymbol member,
        Compilation compilation,
        out string? converterFq,
        out string? storageReader,
        out Location? mapLocation)
    {
        converterFq = null;
        storageReader = null;
        mapLocation = null;
        InvocationExpressionSyntax? mapCall = null;
        foreach (var syntaxRef in member.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is not PropertyDeclarationSyntax property
                || property.Initializer?.Value is not InvocationExpressionSyntax invocation)
            {
                continue;
            }

            while (invocation.Expression is MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax inner } access)
            {
                if (access.Name.Identifier.Text == "Map")
                {
                    mapCall = invocation;
                }

                invocation = inner;
            }

            break;
        }

        if (mapCall is null)
        {
            return MapStatus.None;
        }

        mapLocation = mapCall.GetLocation();
        if (member.Type is INamedTypeSymbol columnType
            && TryColumn(columnType, out var resultType, out _)
            && IsNullableResult(resultType))
        {
            return MapStatus.NullableResult;
        }

        // Timestamp means different things per dialect: SQL Server rowversion is
        // byte[], Postgres timestamp-without-zone is a DateTime.
        var isSqlServerColumn = member.Type is INamedTypeSymbol storageType
            && TryColumn(storageType, out _, out var sqlColumn)
            && sqlColumn;

        storageReader = FactoryName(member) switch
        {
            "Timestamp" => isSqlServerColumn ? "GetFieldValue<byte[]>" : "GetDateTime",
            "Text" or "NText" or "NVarChar" or "NVarCharMax" or "Char" or "VarChar" or "Varchar" => "GetString",
            "Json" or "Jsonb" => "GetString",
            "Integer" or "Int" or "Identity" => "GetInt32",
            "SmallInt" => "GetInt16",
            "TinyInt" => "GetByte",
            "BigInt" => "GetInt64",
            "Decimal" or "Numeric" or "Money" => "GetDecimal",
            "Real" => "GetFloat",
            "Float" or "DoublePrecision" => "GetDouble",
            "Boolean" or "Bit" => "GetBoolean",
            "DateTime" or "DateTime2" => "GetDateTime",
            "Timestamptz" => "GetFieldValue<global::System.DateTimeOffset>",
            "Date" => "GetFieldValue<global::System.DateOnly>",
            "Time" => "GetFieldValue<global::System.TimeOnly>",
            "Uuid" or "UniqueIdentifier" => "GetGuid",
            "Bytea" => "GetFieldValue<byte[]>",
            _ => null
        };
        if (storageReader is null || mapCall.ArgumentList.Arguments.Count < 2)
        {
            return MapStatus.Invalid;
        }

        var readArg = mapCall.ArgumentList.Arguments[0].Expression;
        var model = compilation.GetSemanticModel(readArg.SyntaxTree);
        var info = model.GetSymbolInfo(readArg);
        var method = info.Symbol as IMethodSymbol
            ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (method is not { IsStatic: true })
        {
            return MapStatus.Invalid;
        }

        converterFq = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + method.Name;
        return MapStatus.Valid;
    }

    // A column's type argument is the storage type, so any nullability on it
    // collides with IsRequired. Nullable<T> would emit "Guid??"; an annotated
    // reference type is erased by the display format and merely misleading.
    public static bool IsNullableResult(ITypeSymbol type)
        => type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            || type.NullableAnnotation == NullableAnnotation.Annotated;

    // The type MIZ009 tells the user to map to instead.
    public static ITypeSymbol NonNullableResult(ITypeSymbol type)
        => type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? ((INamedTypeSymbol)type).TypeArguments[0]
            : type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

    // True when a column on this table already reported MIZ008/MIZ009. The
    // projection generator suppresses MIZ007 in that case: the column
    // diagnostic is the actionable one and points at the real line.
    public static bool HasReportedColumnError(INamedTypeSymbol symbol, Compilation compilation)
    {
        if (symbol.IsAbstract || !IsDialectTable(symbol, out _))
        {
            return false;
        }

        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsStatic
                || member.Type is not INamedTypeSymbol type
                || !TryColumn(type, out _, out _))
            {
                continue;
            }

            if (GetMapInfo(member, compilation, out _, out _, out _) is MapStatus.Invalid or MapStatus.NullableResult)
            {
                return true;
            }
        }

        return false;
    }

    // Names of the fluent modifiers chained after the factory call, e.g.
    // Text("email").NotNull().PrimaryKey() -> ["PrimaryKey", "NotNull"].
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

    public static string ReaderCall(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Int32 => "GetInt32",
        SpecialType.System_String => "GetString",
        SpecialType.System_Int64 => "GetInt64",
        SpecialType.System_Boolean => "GetBoolean",
        SpecialType.System_DateTime => "GetDateTime",
        SpecialType.System_Double => "GetDouble",
        SpecialType.System_Decimal => "GetDecimal",
        SpecialType.System_Int16 => "GetInt16",
        SpecialType.System_Byte => "GetByte",
        SpecialType.System_Single => "GetFloat",
        _ when type.ToDisplayString() == "System.Guid" => "GetGuid",
        _ => $"GetFieldValue<{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>"
    };

    // Inverse of ToCSharpType: recovers a symbol so callers can ask the
    // compilation about conversions. Returns null for shapes it cannot round-trip
    // (arrays, say), which callers treat as "unknown, don't complain".
    public static ITypeSymbol? ResolveClrType(string clrTypeName, Compilation compilation) => clrTypeName switch
    {
        "int" => compilation.GetSpecialType(SpecialType.System_Int32),
        "string" => compilation.GetSpecialType(SpecialType.System_String),
        "long" => compilation.GetSpecialType(SpecialType.System_Int64),
        "bool" => compilation.GetSpecialType(SpecialType.System_Boolean),
        "System.DateTime" => compilation.GetSpecialType(SpecialType.System_DateTime),
        "double" => compilation.GetSpecialType(SpecialType.System_Double),
        "decimal" => compilation.GetSpecialType(SpecialType.System_Decimal),
        "short" => compilation.GetSpecialType(SpecialType.System_Int16),
        "byte" => compilation.GetSpecialType(SpecialType.System_Byte),
        "float" => compilation.GetSpecialType(SpecialType.System_Single),
        _ => compilation.GetTypeByMetadataName(
            clrTypeName.StartsWith("global::", StringComparison.Ordinal) ? clrTypeName.Substring(8) : clrTypeName)
    };

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

    private static bool TryCtorLiterals(INamedTypeSymbol symbol, out string tableName, out string? schema)
    {
        tableName = "";
        schema = null;
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
                if (args.Count is < 1 or > 2)
                {
                    return false;
                }

                // base(name, schema), honoring named arguments.
                for (var i = 0; i < args.Count; i++)
                {
                    if (args[i].Expression is not LiteralExpressionSyntax literal
                        || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        return false;
                    }

                    var value = literal.Token.ValueText;
                    var parameter = args[i].NameColon?.Name.Identifier.Text
                        ?? i switch { 0 => "name", _ => "schema" };
                    switch (parameter)
                    {
                        case "name":
                            tableName = value;
                            break;
                        case "schema":
                            schema = value;
                            break;
                        default:
                            return false;
                    }
                }

                return tableName.Length > 0;
            }
        }

        return false;
    }
}
