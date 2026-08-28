using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mizzle.Generators;

// Reconstructs a BakedQuerySpec from a statically-visible fluent chain:
//   db.Select(t.Col, u.Col, ...)
//     .From(t) | .From(t.ToFrom())
//     [.InnerJoin(u).On(cond, ...)] [.LeftJoin(u).On(cond, ...)]
//     [.InnerJoin(u, cond)] [.LeftJoin(u, cond)]           (legacy forms)
//     [.Where(cond, ...)] [.Where(t.Col, value)]           (repeatable, AND-combined)
//     [.OrderBy(t.Col)] [.OrderByDesc(t.Col)] [.OrderBy(t.Col.ToRef())]
//     [.Distinct()] [.Limit(<literal>)] [.Offset(<literal>)] [.Page(<literal>, <literal>)]
//     .ToListAsync(...)
// Conditions must be X.Eq(Y): column-vs-column or column-vs-runtime-bind.
// Returns null for anything the generator cannot prove at compile time.
internal static class BakedChainWalker
{
    public static BakedQuerySpec? TryGetSpec(InvocationExpressionSyntax terminator, SemanticModel model)
        => TryGetSpec(terminator, model, out _);

    // hasReportedColumnError: a referenced table failed to resolve because one
    // of its columns already reported MIZ008/MIZ009. Callers use it to stay
    // quiet rather than pile a misleading MIZ007 on top of the real error.
    public static BakedQuerySpec? TryGetSpec(
        InvocationExpressionSyntax terminator,
        SemanticModel model,
        out bool hasReportedColumnError)
    {
        if (terminator.Expression is not MemberAccessExpressionSyntax terminatorMember)
        {
            hasReportedColumnError = false;
            return null;
        }

        return WalkChain(terminatorMember.Expression, model, out hasReportedColumnError);
    }

    // Walks a builder-valued chain that has no terminator of its own -- a union
    // branch, or the body behind a Build().
    public static BakedQuerySpec? WalkChain(
        ExpressionSyntax chain,
        SemanticModel model,
        out bool hasReportedColumnError)
    {
        hasReportedColumnError = false;
        var calls = new List<(string Name, InvocationExpressionSyntax Invocation)>();
        var current = chain;
        while (true)
        {
            while (current is InvocationExpressionSyntax invocation)
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax member)
                {
                    return null;
                }

                calls.Add((member.Name.Identifier.Text, invocation));
                current = member.Expression;
            }

            // A chain may be composed across locals -- var q = db.Select(...); q.Where(...)
            // -- so splice the local's own chain in and keep walking.
            if (ResolveBuilderLocal(current, model) is not { } spliced)
            {
                break;
            }

            current = spliced;
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
        var state = new WalkState(model);
        try
        {
        for (var i = 0; i < calls.Count; i++)
        {
            var (name, invocation) = calls[i];
            var args = invocation.ArgumentList.Arguments;
            switch (name)
            {
                case "Select" when i == 0 && args.Count > 0:
                    foreach (var arg in args)
                    {
                        var item = state.ResolveColumn(arg.Expression)
                            ?? state.ResolveSelectExpression(arg.Expression);
                        if (item is null)
                        {
                            return null;
                        }

                        state.Select.Add(item);
                    }

                    break;
                case "GroupBy" when args.Count > 0:
                    foreach (var arg in args)
                    {
                        if (state.ResolveColumn(arg.Expression) is not { } grouped)
                        {
                            return null;
                        }

                        state.GroupBy.Add((grouped.TableAlias, grouped.DbName));
                    }

                    break;
                case "From" when args.Count == 1 && state.From is null:
                    if (state.ResolveTable(Unwrap(args[0].Expression)) is not { } fromTable)
                    {
                        return null;
                    }

                    state.From = fromTable;
                    break;
                case "InnerJoin" or "LeftJoin" when args.Count == 1 && state.ResolveTable(args[0].Expression) is { } joinTable:
                    // JoinBuilder form: the next chain call must be On(...)
                    if (i + 1 >= calls.Count || calls[i + 1].Name != "On")
                    {
                        return null;
                    }

                    var onArgs = calls[i + 1].Invocation.ArgumentList.Arguments;
                    if (onArgs.Count == 0)
                    {
                        return null;
                    }

                    var conditions = new List<BakedCondition>();
                    foreach (var onArg in onArgs)
                    {
                        if (state.ResolveCondition(onArg.Expression) is not { } condition)
                        {
                            return null;
                        }

                        conditions.Add(condition);
                    }

                    state.Joins.Add(new BakedJoin(name == "LeftJoin", joinTable, conditions));
                    i++; // consume the On call
                    break;
                case "InnerJoin" or "LeftJoin" when args.Count == 2:
                    if (state.ResolveTable(Unwrap(args[0].Expression)) is not { } legacyTable)
                    {
                        return null;
                    }

                    var legacyConditions = new List<BakedCondition>();
                    if (!state.TryFlattenConditions(args[1].Expression, legacyConditions))
                    {
                        return null;
                    }

                    state.Joins.Add(new BakedJoin(name == "LeftJoin", legacyTable, legacyConditions));
                    break;
                case "Where" when args.Count == 2
                    && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Parameters.Length: 2 } whereMethod
                    && whereMethod.Parameters[0].Type.Name == "IColumn":
                    if (state.ResolveColumn(args[0].Expression) is not { } whereColumn)
                    {
                        return null;
                    }

                    state.Where.Add(new BakedCondition(whereColumn.TableAlias, whereColumn.DbName, null, null));
                    break;
                case "Where" when args.Count >= 1:
                    foreach (var arg in args)
                    {
                        if (state.ResolveCondition(arg.Expression) is not { } condition)
                        {
                            return null;
                        }

                        state.Where.Add(condition);
                    }

                    break;
                case "OrderBy" or "OrderByDesc" when args.Count == 1:
                    var orderExpr = args[0].Expression is InvocationExpressionSyntax
                        {
                            Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "ToRef" } toRefMember
                        }
                        ? toRefMember.Expression
                        : args[0].Expression;
                    if (state.ResolveColumn(orderExpr) is not { } orderColumn)
                    {
                        return null;
                    }

                    state.OrderBy.Add((orderColumn.TableAlias, orderColumn.DbName, name == "OrderByDesc"));
                    break;
                case "Limit" when args.Count == 1 && state.Limit is null && TryIntLiteral(args[0].Expression, out var limitValue):
                    state.Limit = limitValue;
                    break;
                case "Offset" when args.Count == 1 && state.Offset is null && TryIntLiteral(args[0].Expression, out var offsetValue):
                    state.Offset = offsetValue;
                    break;
                case "Page" when args.Count == 2
                    && state.Limit is null
                    && state.Offset is null
                    && TryIntLiteral(args[0].Expression, out var page)
                    && TryIntLiteral(args[1].Expression, out var pageSize)
                    && page >= 1:
                    state.Limit = pageSize;
                    state.Offset = (page - 1) * pageSize;
                    break;
                case "With" or "WithRecursive" when args.Count == 1:
                    if (state.ResolveCte(args[0].Expression) is not { } cte)
                    {
                        return null;
                    }

                    state.With.Add(cte);
                    state.RecursiveWith |= name == "WithRecursive";
                    break;
                case "Having" when args.Count == 1:
                    if (state.ResolveHavingCondition(args[0].Expression) is not { } having)
                    {
                        return null;
                    }

                    state.Having.Add(having);
                    break;
                case "UnionAll" when args.Count == 1:
                    if (state.ResolveUnionBranch(args[0].Expression) is not { } branch)
                    {
                        return null;
                    }

                    state.UnionAll.Add(branch);
                    break;
                case "Distinct" when args.Count == 0:
                    state.Distinct = true;
                    break;
                default:
                    return null;
            }
        }

        if (state.From is null || state.Select.Count == 0)
        {
            return null;
        }

        // Every referenced table must share the receiver's dialect.
        if (state.Tables.Values.Any(t => t.IsPostgres != isPostgres.Value))
        {
            return null;
        }

        // Left-joined tables' columns are nullable in the projection.
        var leftAliases = new HashSet<string>(state.Joins.Where(j => j.IsLeft).Select(j => j.Table.Alias));
        var select = state.Select
            .Select(c => leftAliases.Contains(c.TableAlias)
                ? new BakedColumn(c.TableAlias, c.DbName, c.PropertyName, c.ClrTypeName, false, c.ReaderCall, c.ReadConverter, c.ProjectionName, c.IsUntrimmed)
                : c)
            .ToList();

        return new BakedQuerySpec(
            isPostgres.Value,
            state.From,
            state.Joins,
            select,
            state.Distinct,
            state.Where,
            state.OrderBy,
            state.Limit,
            state.Offset,
            state.With,
            state.RecursiveWith,
            state.GroupBy,
            state.Having,
            state.UnionAll);
        }
        finally
        {
            hasReportedColumnError = state.HasReportedColumnError;
        }
    }

    // The Returning(...) columns of an insert/update/delete chain. Enough to
    // validate a typed write projection at compile time; the SQL itself is still
    // emitted at runtime, so this does not produce a BakedQuerySpec.
    public static IReadOnlyList<BakedColumn>? TryGetReturningColumns(
        InvocationExpressionSyntax terminator,
        SemanticModel model)
    {
        if (terminator.Expression is not MemberAccessExpressionSyntax terminatorMember)
        {
            return null;
        }

        var state = new WalkState(model);
        for (var current = terminatorMember.Expression; current is InvocationExpressionSyntax invocation;)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax member)
            {
                return null;
            }

            if (member.Name.Identifier.Text == "Returning")
            {
                var columns = new List<BakedColumn>();
                foreach (var argument in invocation.ArgumentList.Arguments)
                {
                    if (state.ResolveColumn(argument.Expression) is not { } column)
                    {
                        return null;
                    }

                    columns.Add(column);
                }

                return columns.Count == 0 ? null : columns;
            }

            current = member.Expression;
        }

        return null;
    }

    // The chain behind a builder-valued local or field. Returns null unless the
    // symbol is assigned exactly once, at its declaration: a later reassignment
    // (q = q.Where(...)) would otherwise be silently dropped from the baked SQL,
    // producing a query missing a filter.
    private static ExpressionSyntax? ResolveBuilderLocal(ExpressionSyntax expression, SemanticModel model)
    {
        if (expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax)
            || model.GetTypeInfo(expression).Type?.ToDisplayString() != "Mizzle.Fluent.SelectBuilder")
        {
            return null;
        }

        var symbol = model.GetSymbolInfo(expression).Symbol;
        if (symbol is not (ILocalSymbol or IFieldSymbol))
        {
            return null;
        }

        var declaration = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var initializer = declaration switch
        {
            VariableDeclaratorSyntax variable => variable.Initializer?.Value,
            PropertyDeclarationSyntax property => property.Initializer?.Value,
            _ => null
        };

        if (initializer is not InvocationExpressionSyntax chain || IsReassigned(symbol, declaration!, model))
        {
            return null;
        }

        return chain;
    }

    private static bool IsReassigned(ISymbol symbol, SyntaxNode declaration, SemanticModel model)
    {
        var scope = declaration.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (scope is null)
        {
            return true;
        }

        foreach (var assignment in scope.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(assignment.Left).Symbol, symbol))
            {
                return true;
            }
        }

        return false;
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
        => expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "ToFrom" } member
            }
            ? member.Expression
            : expression;

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

    private sealed class WalkState
    {
        private readonly SemanticModel _model;

        public WalkState(SemanticModel model) => _model = model;

        public Dictionary<ISymbol, TableFactsModel> Tables { get; } = new(SymbolEqualityComparer.Default);
        public bool HasReportedColumnError { get; private set; }
        public BakedTable? From { get; set; }
        public List<BakedJoin> Joins { get; } = [];
        public List<BakedColumn> Select { get; } = [];
        public List<BakedCondition> Where { get; } = [];
        public List<(string Alias, string DbName, bool Desc)> OrderBy { get; } = [];
        public List<BakedCte> With { get; } = [];
        public List<(string Alias, string DbName)> GroupBy { get; } = [];
        public List<BakedCondition> Having { get; } = [];
        public List<BakedQuerySpec> UnionAll { get; } = [];
        public bool RecursiveWith { get; set; }
        public int? Limit { get; set; }
        public int? Offset { get; set; }
        public bool Distinct { get; set; }

        // A table as written at the query site: its facts, plus whichever alias
        // this instance carries. Null when the chain cannot be baked.
        public BakedTable? ResolveTable(ExpressionSyntax expression)
        {
            if (_model.GetTypeInfo(expression).Type is not INamedTypeSymbol type
                || ResolveTableBySymbol(type) is not { } facts)
            {
                return null;
            }

            return TryResolveInstanceAlias(expression, out var alias)
                ? new BakedTable(facts, alias ?? facts.Alias)
                : null;
        }

        // var x = new T().WithAlias("q");  or  static readonly T X = new T().WithAlias("q");
        // Reads the alias off the instance's own declaration, so a local and a
        // static field behave identically. Returns false when a WithAlias is
        // present but its argument is not a literal -- the chain then falls back
        // to the runtime path rather than baking a wrong alias.
        public bool TryResolveInstanceAlias(ExpressionSyntax receiver, out string? alias)
        {
            alias = null;
            var symbol = _model.GetSymbolInfo(receiver).Symbol;
            if (symbol is not (ILocalSymbol or IFieldSymbol or IPropertySymbol))
            {
                return true;
            }

            foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
            {
                var initializer = syntaxRef.GetSyntax() switch
                {
                    VariableDeclaratorSyntax variable => variable.Initializer?.Value,
                    PropertyDeclarationSyntax property => property.Initializer?.Value,
                    _ => null
                };

                for (var call = initializer as InvocationExpressionSyntax; call is not null;)
                {
                    if (call.Expression is not MemberAccessExpressionSyntax member)
                    {
                        break;
                    }

                    if (member.Name.Identifier.Text == "WithAlias")
                    {
                        if (call.ArgumentList.Arguments.Count != 1
                            || call.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax literal
                            || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                        {
                            return false;
                        }

                        alias = literal.Token.ValueText;
                        return true;
                    }

                    call = member.Expression as InvocationExpressionSyntax;
                }
            }

            return true;
        }

        // Sql.Eq(<aggregate>, <value>) -- the only HAVING shape that bakes today.
        public BakedCondition? ResolveHavingCondition(ExpressionSyntax expression)
        {
            if (expression is not InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Eq" } member,
                    ArgumentList.Arguments: { Count: 2 } args
                }
                || _model.GetSymbolInfo(member).Symbol is not IMethodSymbol { ContainingType.Name: "Sql" })
            {
                return null;
            }

            var left = ResolveAggregateSql(args[0].Expression);
            return left is null ? null : new BakedCondition("", "", null, null, left);
        }

        // .UnionAll(<select chain>) -- inline, or a local or field holding one.
        public BakedQuerySpec? ResolveUnionBranch(ExpressionSyntax expression)
        {
            var chain = expression as InvocationExpressionSyntax ?? ResolveDeclaredChain(expression);
            return chain is null ? null : WalkChain(chain, _model, out _);
        }

        // A local or field holding a builder chain, e.g. var closed = db.Select(...)...
        private ExpressionSyntax? ResolveDeclaredChain(ExpressionSyntax expression)
        {
            var symbol = _model.GetSymbolInfo(expression).Symbol;
            if (symbol is not (ILocalSymbol or IFieldSymbol))
            {
                return null;
            }

            foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
            {
                var initializer = syntaxRef.GetSyntax() switch
                {
                    VariableDeclaratorSyntax variable => variable.Initializer?.Value,
                    PropertyDeclarationSyntax property => property.Initializer?.Value,
                    _ => null
                };

                if (initializer is InvocationExpressionSyntax declared)
                {
                    return declared;
                }
            }

            return null;
        }

        // Sql.As(Sql.Count(), "N") or a bare Sql.Min(t.Col). The SQL is rendered
        // here; the CLR type is left to the projection target, because an
        // aggregate's result type differs per dialect (count is bigint on
        // Postgres, int on SQL Server).
        public BakedColumn? ResolveSelectExpression(ExpressionSyntax expression)
            => ResolveSelectExpressionCore(expression, requireAlias: true);

        private BakedColumn? ResolveSelectExpressionCore(ExpressionSyntax expression, bool requireAlias)
        {
            string? alias = null;
            if (expression is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "As" } asMember,
                    ArgumentList.Arguments: { Count: 2 } asArgs
                }
                && _model.GetSymbolInfo(asMember).Symbol is IMethodSymbol { ContainingType.Name: "Sql" })
            {
                if (asArgs[1].Expression is not LiteralExpressionSyntax literal
                    || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    return null;
                }

                alias = literal.Token.ValueText;
                expression = asArgs[0].Expression;
            }

            if (expression is not InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax aggregateMember,
                    ArgumentList.Arguments: var aggregateArgs
                }
                || _model.GetSymbolInfo(aggregateMember).Symbol is not IMethodSymbol { ContainingType.Name: "Sql" })
            {
                return null;
            }

            // Sql.Value(x) projects a constant. The placeholder takes a bind slot in
            // select-item order; the value itself comes from the builder at run time.
            if (aggregateMember.Name.Identifier.Text == "Value" && alias is not null)
            {
                return new BakedColumn(
                    "", "", alias, "object", isRequired: false, "GetFieldValue<object>",
                    projectionName: alias,
                    isLiteral: true);
            }

            var function = aggregateMember.Name.Identifier.Text switch
            {
                "Count" => "count",
                "Sum" => "sum",
                "Avg" => "avg",
                "Min" => "min",
                "Max" => "max",
                _ => null
            };
            if (function is null || (requireAlias && alias is null))
            {
                // In a select list an unaliased aggregate has no member to bind to;
                // in HAVING there is nothing to name.
                return null;
            }

            string argument;
            if (aggregateArgs.Count == 0)
            {
                argument = "*";
            }
            else if (aggregateArgs.Count == 1 && ResolveColumn(aggregateArgs[0].Expression) is { } inner)
            {
                argument = Quote(inner.TableAlias) + "." + Quote(inner.DbName);
            }
            else
            {
                return null;
            }

            return new BakedColumn(
                "", "", alias ?? "", "object", isRequired: false, "GetFieldValue<object>",
                projectionName: alias,
                sqlExpression: $"{function}({argument})");
        }

        // The rendered SQL for a bare aggregate call, without any alias.
        public string? ResolveAggregateSql(ExpressionSyntax expression)
            => ResolveSelectExpressionCore(expression, requireAlias: false)?.SqlExpression;

        // The emitter quotes per dialect; the walker knows the dialect only from
        // the resolved tables, so mirror it from the first one seen.
        private string Quote(string identifier)
            => Tables.Values.FirstOrDefault()?.IsPostgres == false
                ? "[" + identifier.Replace("]", "]]") + "]"
                : "\"" + identifier.Replace("\"", "\"\"") + "\"";

        // CteBuilder.Named("name", <select chain>.Build()) -- the body is walked
        // with the same machinery as the outer query. A non-literal name, or a
        // body that cannot be baked, makes the whole chain unbakeable.
        public BakedCte? ResolveCte(ExpressionSyntax expression)
        {
            if (expression is not InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Named" },
                    ArgumentList.Arguments: { Count: 2 } namedArgs
                })
            {
                return null;
            }

            if (namedArgs[0].Expression is not LiteralExpressionSyntax nameLiteral
                || !nameLiteral.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return null;
            }

            var bodyExpression = Unwrap(namedArgs[1].Expression);
            if (ResolveBuildChain(bodyExpression) is not { } buildInvocation)
            {
                return null;
            }

            var body = TryGetSpec(buildInvocation, _model, out _);
            return body is null ? null : new BakedCte(nameLiteral.Token.ValueText, body);
        }

        // The CTE body is written inline as db.Select(...)....Build(), or held in
        // a local/field initialized that way.
        private InvocationExpressionSyntax? ResolveBuildChain(ExpressionSyntax expression)
        {
            if (expression is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Build" }
                } inline)
            {
                return inline;
            }

            if (_model.GetSymbolInfo(expression).Symbol is not (ILocalSymbol or IFieldSymbol))
            {
                return null;
            }

            foreach (var syntaxRef in _model.GetSymbolInfo(expression).Symbol!.DeclaringSyntaxReferences)
            {
                var initializer = syntaxRef.GetSyntax() switch
                {
                    VariableDeclaratorSyntax variable => variable.Initializer?.Value,
                    PropertyDeclarationSyntax property => property.Initializer?.Value,
                    _ => null
                };

                if (initializer is InvocationExpressionSyntax
                    {
                        Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Build" }
                    } declared)
                {
                    return declared;
                }
            }

            return null;
        }

        // t.Col -> (table facts, column fact) as a BakedColumn.
        public BakedColumn? ResolveColumn(ExpressionSyntax expression)
        {
            string? projectionName = null;
            if (expression is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "As" } asMember,
                    ArgumentList.Arguments: { Count: 1 } asArgs
                })
            {
                if (asArgs[0].Expression is not LiteralExpressionSyntax asLiteral
                    || !asLiteral.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    return null;
                }

                projectionName = asLiteral.Token.ValueText;
                expression = asMember.Expression;
            }

            if (expression is not MemberAccessExpressionSyntax
                || _model.GetSymbolInfo(expression).Symbol is not IPropertySymbol property
                || property.Type is not INamedTypeSymbol propertyType
                || !TableFacts.TryColumn(propertyType, out _, out _))
            {
                return null;
            }

            if (ResolveTableBySymbol(property.ContainingType) is not { } facts)
            {
                return null;
            }

            // The alias belongs to the instance the column was reached through,
            // not to the table type -- two instances differ only by alias.
            if (!TryResolveInstanceAlias(((MemberAccessExpressionSyntax)expression).Expression, out var instanceAlias))
            {
                return null;
            }

            var fact = facts.Columns.FirstOrDefault(c => c.PropertyName == property.Name);
            return fact is null
                ? null
                : new BakedColumn(instanceAlias ?? facts.Alias, fact.DbName, fact.PropertyName, fact.ClrTypeName, fact.IsRequired, fact.ReaderCall, fact.ReadConverter, projectionName, fact.IsUntrimmed);
        }

        // X.Eq(Y): column vs column, or column vs runtime bind.
        public BakedCondition? ResolveCondition(ExpressionSyntax expression)
        {
            if (expression is not InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Eq" } eqMember,
                    ArgumentList.Arguments: { Count: 1 } eqArgs
                })
            {
                return null;
            }

            if (ResolveColumn(eqMember.Expression) is not { } left)
            {
                return null;
            }

            var right = ResolveColumn(eqArgs[0].Expression);
            return right is null
                ? new BakedCondition(left.TableAlias, left.DbName, null, null)
                : new BakedCondition(left.TableAlias, left.DbName, right.TableAlias, right.DbName);
        }

        // Flattens Sql.And(...) trees in a legacy join's single Expr argument.
        public bool TryFlattenConditions(ExpressionSyntax expression, List<BakedCondition> into)
        {
            if (expression is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "And" } andMember
                } andCall
                && _model.GetSymbolInfo(andCall).Symbol is IMethodSymbol { ContainingType.Name: "Sql" }
                && andMember.Expression is IdentifierNameSyntax or MemberAccessExpressionSyntax)
            {
                foreach (var arg in andCall.ArgumentList.Arguments)
                {
                    if (!TryFlattenConditions(arg.Expression, into))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (ResolveCondition(expression) is { } condition)
            {
                into.Add(condition);
                return true;
            }

            return false;
        }

        private TableFactsModel? ResolveTableBySymbol(INamedTypeSymbol type)
        {
            if (Tables.TryGetValue(type, out var known))
            {
                return known;
            }

            var facts = TableFacts.FromSymbol(type, _model.Compilation);
            if (facts is null)
            {
                if (TableFacts.HasReportedColumnError(type, _model.Compilation))
                {
                    HasReportedColumnError = true;
                }

                return null;
            }

            Tables[type] = facts;
            return facts;
        }
    }
}
