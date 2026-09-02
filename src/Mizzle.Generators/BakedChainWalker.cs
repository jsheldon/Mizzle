using System.Text;
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

    private static readonly HashSet<string> WriteTerminators = new(StringComparer.Ordinal)
    {
        "ExecuteAsync",
        "ToListAsync",
        "FirstAsync",
        "FirstOrDefaultAsync",
        "SingleAsync",
        "SingleOrDefaultAsync",
    };

    // SELECT/UPDATE/DELETE terminator whose shape is statically visible enough
    // to know which tables are in play and which columns WHERE names. Null when
    // the chain is not a terminator, or cannot be proven -- callers stay silent.
    public static AlwaysFilterQuery? TryGetAlwaysFilterQuery(
        InvocationExpressionSyntax terminator,
        SemanticModel model)
    {
        if (model.GetSymbolInfo(terminator).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        var containing = method.ContainingType.ToDisplayString();
        if (containing == "Mizzle.Fluent.SelectBuilder")
        {
            if (!QueryInterceptability.IsQueryTerminator(method))
            {
                return null;
            }

            var spec = TryGetSpec(terminator, model);
            return spec is null ? null : FromSelectSpec(spec);
        }

        if (containing is not ("Mizzle.Fluent.UpdateBuilder" or "Mizzle.Fluent.DeleteBuilder")
            || !WriteTerminators.Contains(method.Name)
            || terminator.Expression is not MemberAccessExpressionSyntax writeMember)
        {
            return null;
        }

        return WalkWriteChain(writeMember.Expression, model, containing == "Mizzle.Fluent.UpdateBuilder");
    }

    private static AlwaysFilterQuery FromSelectSpec(BakedQuerySpec spec)
    {
        var tables = new List<BakedTable> { spec.From };
        foreach (var join in spec.Joins)
        {
            tables.Add(join.Table);
        }

        // A union branch and a CTE body are each their own scope: the outer
        // WHERE does not constrain them, so they are checked as nested queries
        // carrying only their own predicates.
        var where = spec.Where.Where(c => c.ConditionalIndex is null).ToList();
        var nested = spec.UnionAll
            .Concat(spec.With.Select(cte => cte.Body))
            .Select(FromSelectSpec)
            .ToList();
        return new AlwaysFilterQuery(tables, where, nested);
    }

    private static AlwaysFilterQuery? WalkWriteChain(ExpressionSyntax chain, SemanticModel model, bool isUpdate)
    {
        var calls = new List<(string Name, InvocationExpressionSyntax Invocation)>();
        var current = chain;
        while (current is InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax member)
            {
                return null;
            }

            calls.Add((member.Name.Identifier.Text, invocation));
            current = member.Expression;
        }

        var receiverType = model.GetTypeInfo(current).Type?.ToDisplayString();
        if (receiverType is not ("Mizzle.Postgres.PostgresDb" or "Mizzle.SqlServer.SqlDb"))
        {
            return null;
        }

        calls.Reverse();
        var state = new WalkState(model);
        var nested = new List<AlwaysFilterQuery>();
        BakedTable? table = null;
        foreach (var (name, invocation) in calls)
        {
            var args = invocation.ArgumentList.Arguments;
            switch (name)
            {
                case "Update" when isUpdate && table is null && args.Count == 1:
                case "DeleteFrom" when !isUpdate && table is null && args.Count == 1:
                    table = state.ResolveTable(Unwrap(args[0].Expression));
                    if (table is null)
                    {
                        return null;
                    }

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
                case "With" or "WithRecursive" when args.Count == 1:
                    // A CTE body that will not resolve is skipped rather than
                    // abandoning the whole chain: the target table is still
                    // worth checking.
                    if (state.ResolveCte(args[0].Expression) is { } writeCte)
                    {
                        nested.Add(FromSelectSpec(writeCte.Body));
                    }

                    break;
                case "Set" or "Returning" or "Expect" or "Timeout":
                    break;
                default:
                    return null;
            }
        }

        return table is null ? null : new AlwaysFilterQuery([table], state.Where, nested);
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
                case "WhereIf" when args.Count == 2:
                    if (state.ResolveCondition(args[1].Expression) is not { } conditional)
                    {
                        return null;
                    }

                    state.Where.Add(conditional.WithConditionalIndex(state.ConditionalCount));
                    state.ConditionalCount++;
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

        if (state.From is null
            || state.Select.Count == 0
            || state.ConditionalCount > BakedSqlEmitter.MaxBakedConditionals)
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
            state.UnionAll,
            state.ConditionalCount);
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
        public int ConditionalCount { get; set; }
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
                if (TryGetAliasName(asArgs[1].Expression) is not { } sqlAlias)
                {
                    return null;
                }

                alias = sqlAlias;
                expression = asArgs[0].Expression;
            }

            if ((ResolveConvertSql(expression)
                 ?? ResolveTSqlCallSql(expression)
                 ?? ResolveCaseSql(expression)) is { } convertSql)
            {
                if (requireAlias && alias is null)
                {
                    return null;
                }

                return new BakedColumn(
                    "", "", alias ?? "", "object", isRequired: false, "GetFieldValue<object>",
                    projectionName: alias,
                    sqlExpression: convertSql);
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

        // A string literal or nameof(...) -- both fold to a compile-time constant,
        // so an alias written as nameof(Row.Member) survives a rename of Member.
        // Anything else (a field, a variable, string concatenation) is not a
        // constant and falls back to the runtime path.
        private string? TryGetAliasName(ExpressionSyntax expression)
            => _model.GetConstantValue(expression) is { HasValue: true, Value: string alias } ? alias : null;

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
                if (TryGetAliasName(asArgs[0].Expression) is not { } asAlias)
                {
                    return null;
                }

                projectionName = asAlias;
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

        // TSql.Convert(SqlType.VarChar(20), TSql.Convert(SqlType.Int, col)) ->
        // CONVERT(varchar(20), CONVERT(int, [alias].[col])). Null when the type
        // is not a literal SqlType or the value is not a column / nested convert.
        public string? ResolveConvertSql(ExpressionSyntax expression)
        {
            if (UnwrapExprLocal(expression) is not InvocationExpressionSyntax invocation
                || _model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol
                {
                    Name: "Convert",
                    ContainingType.Name: "TSql",
                    ContainingType.ContainingNamespace: { } ns
                }
                || ns.ToDisplayString() != "Mizzle.SqlServer"
                || invocation.ArgumentList.Arguments.Count is not (2 or 3))
            {
                return null;
            }

            var arguments = invocation.ArgumentList.Arguments;
            var sqlType = ResolveSqlType(arguments[0].Expression);
            if (sqlType is null)
            {
                return null;
            }

            // The style code is emitted verbatim, so it has to be a literal here
            // for the baked text to match what the runtime emitter produces.
            string style;
            if (arguments.Count == 3)
            {
                if (!TryIntLiteral(arguments[2].Expression, out var styleCode))
                {
                    return null;
                }

                style = ", " + styleCode;
            }
            else
            {
                style = "";
            }

            var inner = ResolveScalarSql(arguments[1].Expression);
            return inner is null ? null : "CONVERT(" + sqlType + ", " + inner + style + ")";
        }

        // TSql.GetDate() -> getdate(); TSql.RTrim(col) -> rtrim([t].[c]). Null for
        // any TSql member the baker cannot render, which keeps the query on the
        // runtime path instead of guessing at its SQL.
        public string? ResolveTSqlCallSql(ExpressionSyntax expression)
        {
            if (UnwrapExprLocal(expression) is not InvocationExpressionSyntax invocation
                || _model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol
                {
                    ContainingType.Name: "TSql",
                    ContainingType.ContainingNamespace: { } ns
                } method
                || ns.ToDisplayString() != "Mizzle.SqlServer")
            {
                return null;
            }

            var arguments = invocation.ArgumentList.Arguments;
            if (TSqlFunctionNames.For(method.Name, arguments.Count) is not { } function)
            {
                return null;
            }

            if (arguments.Count == 0)
            {
                return function + "()";
            }

            var inner = ResolveScalarSql(arguments[0].Expression);
            return inner is null ? null : function + "(" + inner + ")";
        }

        // A scalar position: a column, a CONVERT, or a TSql function over one.
        private string? ResolveScalarSql(ExpressionSyntax expression)
            => ResolveColumn(expression) is { } column
                ? Quote(column.TableAlias) + "." + Quote(column.DbName)
                : ResolveConvertSql(expression) ?? ResolveTSqlCallSql(expression);

        // An Expr held in a local or field -- var key = TSql.Convert(...) -- so a
        // join key used in two places can be written once. Reassignment disqualifies
        // it: the initializer would no longer be what the runtime emits.
        private ExpressionSyntax UnwrapExprLocal(ExpressionSyntax expression)
        {
            if (expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
            {
                return expression;
            }

            var type = _model.GetTypeInfo(expression).Type;
            if (type is null || !IsRenderedOperand(type))
            {
                return expression;
            }

            var symbol = _model.GetSymbolInfo(expression).Symbol;
            if (symbol is not (ILocalSymbol or IFieldSymbol))
            {
                return expression;
            }

            var declaration = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            var initializer = declaration switch
            {
                VariableDeclaratorSyntax variable => variable.Initializer?.Value,
                PropertyDeclarationSyntax property => property.Initializer?.Value,
                _ => null
            };

            return initializer is InvocationExpressionSyntax invocation
                   && !IsReassigned(symbol, declaration!, _model)
                ? invocation
                : expression;
        }

        private string? ResolveSqlType(ExpressionSyntax expression)
        {
            if (_model.GetSymbolInfo(expression).Symbol is IPropertySymbol
                {
                    ContainingType.Name: "SqlType",
                    ContainingType.ContainingNamespace: { } propNs
                } property
                && propNs.ToDisplayString() == "Mizzle.SqlServer")
            {
                return SqlTypeNames.For(property.Name, null);
            }

            if (expression is InvocationExpressionSyntax invocation
                && _model.GetSymbolInfo(invocation).Symbol is IMethodSymbol
                {
                    ContainingType.Name: "SqlType",
                    ContainingType.ContainingNamespace: { } methodNs
                } method
                && methodNs.ToDisplayString() == "Mizzle.SqlServer"
                && invocation.ArgumentList.Arguments.Count == 1
                && invocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal
                && literal.Token.Value is int length)
            {
                return SqlTypeNames.For(method.Name, length);
            }

            return null;
        }

        // X.Eq(Y): column vs column, column vs TSql.Convert, or column vs runtime bind.
        public BakedCondition? ResolveCondition(ExpressionSyntax expression)
        {
            // Sql.And(...)/Sql.Or(...): each argument resolves recursively, in
            // argument order -- SQL AND/OR are associative, so a flat join
            // renders correctly regardless of how the params-array overload
            // folds its runtime Expr tree, as long as the argument order (and
            // so the bind order) matches.
            if (UnwrapExprLocal(expression) is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "And" or "Or" } combinatorMember,
                    ArgumentList.Arguments: { Count: >= 2 } combinatorArgs
                }
                && _model.GetSymbolInfo(combinatorMember).Symbol is IMethodSymbol { ContainingType.Name: "Sql" })
            {
                var children = new List<BakedCondition>(combinatorArgs.Count);
                foreach (var argument in combinatorArgs)
                {
                    if (ResolveCondition(argument.Expression) is not { } child)
                    {
                        return null;
                    }

                    children.Add(child);
                }

                return new BakedCondition(combinatorMember.Name.Identifier.Text.ToUpperInvariant(), children);
            }

            // Sql.Eq(left, right): a free-standing comparison for operands that
            // are not a bare column receiver -- e.g.
            // Sql.Eq(TSql.RTrim(col), Sql.Value("")) inside a composite
            // Sql.And/Sql.Or group, where column.Eq(value) does not fit because
            // the column is wrapped in a rendered expression.
            if (UnwrapExprLocal(expression) is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Eq" } sqlEqMember,
                    ArgumentList.Arguments: { Count: 2 } sqlEqArgs
                }
                && _model.GetSymbolInfo(sqlEqMember).Symbol is IMethodSymbol { ContainingType.Name: "Sql" })
            {
                string leftAlias, leftDbName;
                string? leftExpr = null;
                if (ResolveColumn(sqlEqArgs[0].Expression) is { } leftColumn)
                {
                    leftAlias = leftColumn.TableAlias;
                    leftDbName = leftColumn.DbName;
                }
                else if (ResolveConvertSql(sqlEqArgs[0].Expression) is { } leftConvert)
                {
                    (leftAlias, leftDbName, leftExpr) = ("", "", leftConvert);
                }
                else if (ResolveTSqlCallSql(sqlEqArgs[0].Expression) is { } leftCall)
                {
                    (leftAlias, leftDbName, leftExpr) = ("", "", leftCall);
                }
                else
                {
                    return null;
                }

                if (ResolveColumn(sqlEqArgs[1].Expression) is { } rightColumn)
                {
                    return new BakedCondition(leftAlias, leftDbName, rightColumn.TableAlias, rightColumn.DbName, leftExpr);
                }

                if (ResolveConvertSql(sqlEqArgs[1].Expression) is { } rightConvert)
                {
                    return new BakedCondition(leftAlias, leftDbName, null, null, leftExpr, rightExpression: rightConvert);
                }

                if (ResolveTSqlCallSql(sqlEqArgs[1].Expression) is { } rightCall)
                {
                    return new BakedCondition(leftAlias, leftDbName, null, null, leftExpr, rightExpression: rightCall);
                }

                // Sql.Value(x) is an Expr but means "bind x", same as a bare
                // literal. Any other Column<T>/Expr right side had to be
                // rendered above; reaching here with one means the shape
                // cannot be baked (see the IsRenderedOperand comment further
                // down).
                return !IsSqlValue(sqlEqArgs[1].Expression)
                    && IsRenderedOperand(_model.GetTypeInfo(sqlEqArgs[1].Expression).Type)
                    ? null
                    : new BakedCondition(leftAlias, leftDbName, null, null, leftExpr);
            }

            if (expression is not InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax member,
                    ArgumentList.Arguments: var conditionArgs
                })
            {
                return null;
            }

            // Unary tests take no argument and no bind slot.
            var unary = member.Name.Identifier.Text switch
            {
                "IsNull" => "IS NULL",
                "IsNotNull" => "IS NOT NULL",
                _ => null
            };
            if (unary is not null && conditionArgs.Count == 0)
            {
                return ResolveColumn(member.Expression) is not { } target
                    ? null
                    : new BakedCondition(target.TableAlias, target.DbName, null, null, op: unary, isUnary: true);
            }

            var op = member.Name.Identifier.Text switch
            {
                "Eq" => "=",
                "Ne" => "<>",
                "Gt" => ">",
                "Gte" => ">=",
                "Lt" => "<",
                "Lte" => "<=",
                "Like" => "LIKE",
                _ => null
            };
            if (member.Name.Identifier.Text == "In")
            {
                return ResolveIn(member, conditionArgs);
            }

            if (op is null || conditionArgs.Count != 1)
            {
                return null;
            }

            if (ResolveColumn(member.Expression) is not { } left)
            {
                return null;
            }

            if (ResolveColumn(conditionArgs[0].Expression) is { } right)
            {
                return new BakedCondition(left.TableAlias, left.DbName, right.TableAlias, right.DbName, op: op);
            }

            if (ResolveConvertSql(conditionArgs[0].Expression) is { } convertSql)
            {
                return new BakedCondition(left.TableAlias, left.DbName, null, null, op: op, rightExpression: convertSql);
            }

            if (ResolveTSqlCallSql(conditionArgs[0].Expression) is { } callSql)
            {
                return new BakedCondition(left.TableAlias, left.DbName, null, null, op: op, rightExpression: callSql);
            }

            // Only a value right side -- Eq(T), Like(string) -- binds a parameter.
            // A Column<T> or Expr right side is rendered into the SQL, so one that
            // reaches here is one nothing above could render, and the query has to
            // leave the baked path: emitting "> @p0" for "> len([c])" would run
            // different SQL than the runtime and never supply the bind.
            if (IsRenderedOperand(_model.GetTypeInfo(conditionArgs[0].Expression).Type))
            {
                return null;
            }

            return new BakedCondition(left.TableAlias, left.DbName, null, null, op: op);
        }

        // Expr and Column<T> operands become SQL text; everything else becomes a bind.
        private static bool IsRenderedOperand(ITypeSymbol? type)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                var containing = current.ContainingNamespace?.ToDisplayString();
                if (current.Name == "Expr" && containing == "Mizzle.Ir")
                {
                    return true;
                }

                if (current.Name == "Column" && containing == "Mizzle.Schema")
                {
                    return true;
                }
            }

            return false;
        }

        // col.In(a, b, c) -> [t].[col] IN (?, ?, ?), one bind per value in call
        // order, which is the order Parameterizer captures the haystack.
        private BakedCondition? ResolveIn(
            MemberAccessExpressionSyntax member,
            SeparatedSyntaxList<ArgumentSyntax> arguments)
        {
            if (arguments.Count == 0 || ResolveColumn(member.Expression) is not { } column)
            {
                return null;
            }

            // In(params T[]) called with an array rather than a value list: the
            // element count is not in the syntax, so the SQL cannot be baked.
            foreach (var argument in arguments)
            {
                if (_model.GetTypeInfo(argument.Expression).Type is IArrayTypeSymbol)
                {
                    return null;
                }
            }

            var markers = new string(BakedSqlEmitter.BindMarker, 1);
            var list = string.Join(", ", Enumerable.Repeat(markers, arguments.Count));
            return new BakedCondition(
                column.TableAlias, column.DbName, null, null,
                op: "IN",
                rightExpression: "(" + list + ")");
        }

        // Sql.Case(Sql.When(cond, result), ...).Else(fallback) -> CASE WHEN ... END.
        // Arms render in call order so the bind markers line up with Parameterizer.
        public string? ResolveCaseSql(ExpressionSyntax expression)
        {
            if (UnwrapExprLocal(expression) is not InvocationExpressionSyntax invocation)
            {
                return null;
            }

            ExpressionSyntax? fallback = null;
            if (invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Else" } elseMember
                && _model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { ContainingType.Name: "CaseExpr" }
                && invocation.ArgumentList.Arguments.Count == 1)
            {
                fallback = invocation.ArgumentList.Arguments[0].Expression;
                if (elseMember.Expression is not InvocationExpressionSyntax inner)
                {
                    return null;
                }

                invocation = inner;
            }

            if (_model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol
                {
                    Name: "Case",
                    ContainingType.Name: "Sql"
                }
                || invocation.ArgumentList.Arguments.Count == 0)
            {
                return null;
            }

            var sql = new StringBuilder("CASE");
            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                if (ResolveWhenSql(argument.Expression) is not { } arm)
                {
                    return null;
                }

                sql.Append(arm);
            }

            if (fallback is not null)
            {
                if (ResolveValueSql(fallback) is not { } otherwise)
                {
                    return null;
                }

                sql.Append(" ELSE ").Append(otherwise);
            }

            return sql.Append(" END").ToString();
        }

        private string? ResolveWhenSql(ExpressionSyntax expression)
        {
            if (expression is not InvocationExpressionSyntax invocation
                || _model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol
                {
                    Name: "When",
                    ContainingType.Name: "Sql"
                }
                || invocation.ArgumentList.Arguments.Count != 2)
            {
                return null;
            }

            var condition = ResolveConditionSql(invocation.ArgumentList.Arguments[0].Expression);
            var result = ResolveValueSql(invocation.ArgumentList.Arguments[1].Expression);
            return condition is null || result is null
                ? null
                : " WHEN " + condition + " THEN " + result;
        }

        // A CASE arm's condition, rendered rather than turned into a BakedCondition:
        // it sits inside an expression, so it cannot carry its own bind slots.
        private string? ResolveConditionSql(ExpressionSyntax expression)
        {
            // Composite Sql.And/Sql.Or conditions render through
            // BakedSqlEmitter.Condition for WHERE/WhereIf; this walk-time
            // renderer predates that and does not understand Combinator/Children.
            if (ResolveCondition(expression) is not { } condition
                || condition.ConditionalIndex is not null
                || condition.LeftExpression is not null
                || condition.Combinator is not null)
            {
                return null;
            }

            var left = Quote(condition.LeftAlias) + "." + Quote(condition.LeftDbName);
            if (condition.IsUnary)
            {
                return left + " " + condition.Op;
            }

            if (condition.RightExpression is not null)
            {
                return left + " " + condition.Op + " " + condition.RightExpression;
            }

            var right = condition.IsBind
                ? new string(BakedSqlEmitter.BindMarker, 1)
                : Quote(condition.RightAlias!) + "." + Quote(condition.RightDbName!);
            return left + " " + condition.Op + " " + right;
        }

        // A CASE result: a column, a rendered expression, or a bound literal.
        private string? ResolveValueSql(ExpressionSyntax expression)
        {
            if (ResolveScalarSql(expression) is { } scalar)
            {
                return scalar;
            }

            // Sql.Value(x) is an Expr but means "bind x", so it binds like a bare
            // literal does. Any other Expr had to be rendered above, and was not.
            if (IsSqlValue(expression))
            {
                return new string(BakedSqlEmitter.BindMarker, 1);
            }

            return _model.GetTypeInfo(expression).Type is { } type && IsRenderedOperand(type)
                ? null
                : new string(BakedSqlEmitter.BindMarker, 1);
        }

        private bool IsSqlValue(ExpressionSyntax expression)
            => expression is InvocationExpressionSyntax invocation
               && _model.GetSymbolInfo(invocation).Symbol is IMethodSymbol
               {
                   Name: "Value",
                   ContainingType.Name: "Sql"
               };

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
