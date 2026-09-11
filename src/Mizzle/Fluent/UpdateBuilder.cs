using Mizzle.Ir;
using Mizzle.Schema;

namespace Mizzle.Fluent;

public sealed class UpdateBuilder
{
    private readonly ITable _table;
    private readonly IQueryExecutor? _executor;
    private readonly EquatableList<JoinClause> _joins;
    private readonly EquatableList<(string Column, Expr Value)> _assignments;
    private readonly Expr? _wherePredicate;
    private readonly EquatableList<SelectItem> _returningItems;
    private readonly EquatableList<RuntimeProjectionColumn> _returningColumns;
    private readonly EquatableList<CteClause> _commonTableExpressions;
    private readonly bool _hasRecursiveCte;
    private readonly int? _expectedRowCount;

    public UpdateBuilder(ITable table, IQueryExecutor? executor = null, QueryOptions? overlay = null)
        : this(table, executor, overlay, [], [], null, [], [], [], false, null)
    {
    }

    private UpdateBuilder(
        ITable table,
        IQueryExecutor? executor,
        QueryOptions? overlay,
        EquatableList<JoinClause> joins,
        EquatableList<(string Column, Expr Value)> set,
        Expr? where,
        EquatableList<SelectItem> returning,
        EquatableList<RuntimeProjectionColumn> returningColumns,
        EquatableList<CteClause> with,
        bool recursiveWith,
        int? expect)
    {
        _table = table;
        _executor = executor;
        Overlay = overlay;
        _joins = joins;
        _assignments = set;
        _wherePredicate = where;
        _returningItems = returning;
        _returningColumns = returningColumns;
        _commonTableExpressions = with;
        _hasRecursiveCte = recursiveWith;
        _expectedRowCount = expect;
    }

    public QueryOptions? Overlay { get; }

    // Generic over the column's own type, matching Column<T>.Eq(T): a mismatched
    // value no longer compiles instead of failing only at the database.
    public UpdateBuilder Set<T>(Column<T> column, T value)
        => Copy(set: [.. _assignments, (column.Name, (Expr)column.Bind(value))]);

    /// <summary>
    ///     Sets this column to a computed/server-side expression (e.g. <c>TSql.GetDate()</c>)
    ///     rather than a literal value. Emitted as literal SQL, not a bound parameter -- use this
    ///     for values the database itself must compute that an application-supplied literal
    ///     cannot faithfully substitute for. Prefer the <see cref="Set{T}(Column{T},T)"/> overload
    ///     for ordinary literal values.
    /// </summary>
    public UpdateBuilder Set<T>(Column<T> column, Expr expression)
        => Copy(set: [.. _assignments, (column.Name, expression)]);

    public UpdateBuilder Where(Expr expr)
        => Copy(where: _wherePredicate is null ? expr : Sql.And(_wherePredicate, expr));

    public UpdateBuilder Where<T>(Column<T> column, T value)
        => Where(new BinaryExpr(BinaryOp.Eq, column.ToRef(), column.Bind(value)));

    /// <summary>
    ///     Joins another table into the update (SQL Server only today: <c>UPDATE ... FROM ...
    ///     INNER JOIN ... ON ...</c>). Postgres's <c>UPDATE ... FROM</c> would need a self-join
    ///     rewrite to express the updated table as one side of the join, which isn't implemented.
    /// </summary>
    public UpdateBuilder InnerJoin(FromSource target, Expr on)
        => Copy(joins: [.. _joins, new JoinClause(JoinKind.Inner, target, on)]);

    public UpdateBuilder InnerJoin(ITable target, Expr on) => InnerJoin(target.ToFrom(), on);

    /// <summary>Joins another table into the update, keeping rows with no match (SQL Server only today).</summary>
    public UpdateBuilder LeftJoin(FromSource target, Expr on)
        => Copy(joins: [.. _joins, new JoinClause(JoinKind.Left, target, on)]);

    public UpdateBuilder LeftJoin(ITable target, Expr on) => LeftJoin(target.ToFrom(), on);

    /// <summary>
    ///     The columns to return from the affected rows, read back through the typed
    ///     terminators. <c>As(...)</c> works here as it does in a select list.
    /// </summary>
    public UpdateBuilder Returning(params IColumn[] columns)
        => Copy(
            returning: [.. columns.Select(c => new SelectItem(c.ToRef(), c.ProjectionName))],
            returningColumns: [.. columns.Select(RuntimeProjectionColumn.From)]);

    /// <summary>Prefixes the statement with a common table expression.</summary>
    public UpdateBuilder With(CteClause cte) => Copy(with: [.. _commonTableExpressions, cte]);

    /// <summary>Prefixes the statement with a <c>WITH RECURSIVE</c> common table expression.</summary>
    public UpdateBuilder WithRecursive(CteClause cte)
        => Copy(with: [.. _commonTableExpressions, cte], recursiveWith: true);

    /// <summary>
    ///     The row count this statement must affect. Anything else throws
    ///     <see cref="ConcurrencyException"/> rather than silently succeeding.
    /// </summary>
    public UpdateBuilder Expect(int affectedRows) => Copy(expect: affectedRows);

    public UpdateBuilder Timeout(TimeSpan timeout) => Copy(overlay: new QueryOptions(timeout));

    public UpdateQuery Build()
    {
        EnsureVersionInWhere();
        if (_assignments.Count == 0)
        {
            throw new InvalidOperationException("SET is required.");
        }

        return new UpdateQuery(
            _table.ToFrom(), _joins, _assignments, _wherePredicate, _returningItems, _commonTableExpressions,
            _hasRecursiveCte);
    }

    public Task<IReadOnlyList<T>> ToListAsync<T>(
        Func<System.Data.Common.DbDataReader, T> map,
        CancellationToken cancellationToken = default)
        => Executor().QueryAsync(Build(), map, Overlay, cancellationToken);

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var query = Build();
        var affected = await Executor().ExecuteAsync(query, Overlay, cancellationToken);
        if (_expectedRowCount is int expected && affected != expected)
        {
            throw new ConcurrencyException(expected, affected);
        }

        return affected;
    }


    /// <summary>Runs the query and maps every row to <typeparamref name="T"/>.</summary>
    public Task<IReadOnlyList<T>> ToListAsync<T>(CancellationToken cancellationToken = default)
    {
        EnsureReturningProjection();
        return ToListAsync(reader => RuntimeProjectionMapper.Read<T>(_returningColumns, reader), cancellationToken);
    }

    /// <summary>Runs the query and returns the first row.</summary>
    /// <exception cref="InvalidOperationException">No rows were returned.</exception>
    public async Task<T> FirstAsync<T>(CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync<T>(cancellationToken);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("Sequence contains no elements.");
        }

        return rows[0];
    }

    /// <summary>Runs the query and returns the first row, or <c>default</c> if there are none.</summary>
    public async Task<T?> FirstOrDefaultAsync<T>(CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync<T>(cancellationToken);
        return rows.Count == 0 ? default : rows[0];
    }

    /// <summary>Runs the query and returns the only row.</summary>
    /// <exception cref="InvalidOperationException">Zero or more than one row was returned.</exception>
    public async Task<T> SingleAsync<T>(CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync<T>(cancellationToken);
        return rows.Count switch
        {
            0 => throw new InvalidOperationException("Sequence contains no elements."),
            1 => rows[0],
            _ => throw new InvalidOperationException("Sequence contains more than one element.")
        };
    }

    /// <summary>Runs the query and returns the only row, or <c>default</c> if there are none.</summary>
    /// <exception cref="InvalidOperationException">More than one row was returned.</exception>
    public async Task<T?> SingleOrDefaultAsync<T>(CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync<T>(cancellationToken);
        return rows.Count switch
        {
            0 => default,
            1 => rows[0],
            _ => throw new InvalidOperationException("Sequence contains more than one element.")
        };
    }

    private IQueryExecutor Executor()
        => _executor ?? throw new InvalidOperationException("This query is not bound to a database.");

    private void EnsureReturningProjection()
    {
        if (_returningColumns.Count == 0)
        {
            throw new InvalidOperationException("Typed update projection requires Returning(...).");
        }
    }

    private void EnsureVersionInWhere()
    {
        foreach (var column in _table.Columns)
        {
            if (column.IsVersion && !ContainsColumn(_wherePredicate, column))
            {
                throw new InvalidOperationException("Version column must appear in WHERE");
            }
        }
    }

    private static bool ContainsColumn(Expr? expr, IColumn column)
    {
        if (expr is null)
        {
            return false;
        }

        var expected = column.ToRef();
        return expr switch
        {
            ColumnRef actual =>
                actual.TableAlias == expected.TableAlias && actual.ColumnName == expected.ColumnName,
            BinaryExpr binary => ContainsColumn(binary.Left, column) || ContainsColumn(binary.Right, column),
            UnaryExpr unary => ContainsColumn(unary.Operand, column),
            InExpr inExpression => ContainsColumn(inExpression.Needle, column) || inExpression.Haystack.Any(item => ContainsColumn(item, column)),
            BetweenExpr between =>
                ContainsColumn(between.Value, column)
                || ContainsColumn(between.Lo, column)
                || ContainsColumn(between.Hi, column),
            CoalesceExpr coalesce => coalesce.Args.Any(arg => ContainsColumn(arg, column)),
            AggregateExpr { Arg: not null } aggregate => ContainsColumn(aggregate.Arg, column),
            CallExpr call => call.Args.Any(arg => ContainsColumn(arg, column)),
            ConvertExpr convert => ContainsColumn(convert.Value, column),
            CaseExpr @case => @case.Whens.Any(w => ContainsColumn(w.Condition, column) || ContainsColumn(w.Result, column))
                              || (@case.Fallback is not null && ContainsColumn(@case.Fallback, column)),
            _ => false
        };
    }

    private UpdateBuilder Copy(
        EquatableList<JoinClause>? joins = null,
        EquatableList<(string Column, Expr Value)>? set = null,
        Expr? where = null,
        EquatableList<SelectItem>? returning = null,
        EquatableList<RuntimeProjectionColumn>? returningColumns = null,
        EquatableList<CteClause>? with = null,
        bool? recursiveWith = null,
        int? expect = null,
        QueryOptions? overlay = null)
        => new(
            _table,
            _executor,
            overlay ?? Overlay,
            joins ?? _joins,
            set ?? _assignments,
            where ?? _wherePredicate,
            returning ?? _returningItems,
            returningColumns ?? _returningColumns,
            with ?? _commonTableExpressions,
            recursiveWith ?? _hasRecursiveCte,
            expect ?? _expectedRowCount);
}
