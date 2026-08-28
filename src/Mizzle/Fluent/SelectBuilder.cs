using Mizzle.Ir;
using Mizzle.Paging;
using Mizzle.Schema;
using System.Data.Common;

namespace Mizzle.Fluent;

public sealed class SelectBuilder
{
    private readonly EquatableList<SelectItem> _select;
    private readonly FromSource? _from;
    private readonly EquatableList<JoinClause> _joins;
    private readonly Expr? _where;
    private readonly EquatableList<OrderByItem> _orderBy;
    private readonly int? _limit;
    private readonly int? _offset;
    private readonly bool _distinct;
    private readonly EquatableList<CteClause> _with;
    private readonly bool _recursiveWith;
    private readonly EquatableList<SelectQuery> _unionAll;
    private readonly EquatableList<Expr> _groupBy;
    private readonly Expr? _having;
    private readonly int _conditionalCount;

    private readonly IQueryExecutor? _executor;

    public SelectBuilder(IQueryExecutor? executor = null, QueryOptions? overlay = null)
        : this(executor, overlay, [], null, [], null, [], null, null, false, [], false, [], [], null, 0, 0UL)
    {
    }

    private SelectBuilder(
        IQueryExecutor? executor,
        QueryOptions? overlay,
        EquatableList<SelectItem> select,
        FromSource? from,
        EquatableList<JoinClause> joins,
        Expr? where,
        EquatableList<OrderByItem> orderBy,
        int? limit,
        int? offset,
        bool distinct,
        EquatableList<CteClause> with,
        bool recursiveWith,
        EquatableList<SelectQuery> unionAll,
        EquatableList<Expr> groupBy,
        Expr? having,
        int conditionalCount,
        ulong conditionalMask)
    {
        _executor = executor;
        Overlay = overlay;
        _select = select;
        _from = from;
        _joins = joins;
        _where = where;
        _orderBy = orderBy;
        _limit = limit;
        _offset = offset;
        _distinct = distinct;
        _with = with;
        _recursiveWith = recursiveWith;
        _unionAll = unionAll;
        _groupBy = groupBy;
        _having = having;
        _conditionalCount = conditionalCount;
        ConditionalMask = conditionalMask;
    }

    public QueryOptions? Overlay { get; }

    /// <summary>
    ///     Which <see cref="WhereIf(bool, Expr)"/> predicates were applied, one bit per
    ///     call in chain order. Generated interceptors use it to select the baked SQL
    ///     for this shape; there is no reason to read it otherwise.
    /// </summary>
    public ulong ConditionalMask { get; }

    /// <summary>
    ///     The select list. Columns convert implicitly and carry their <c>As(...)</c>
    ///     alias; expressions -- aggregates, literals, function calls -- are aliased
    ///     with <c>Sql.As(...)</c>. Both can be mixed freely.
    /// </summary>
    /// <example>
    ///     <code>db.Select(o.CustomerId, Sql.Count().As("Orders")).From(o).GroupBy(o.CustomerId)</code>
    /// </example>
    public SelectBuilder Select(params SelectItem[] items) => Copy(select: [..items]);

    /// <summary>Groups the result by the given expressions.</summary>
    public SelectBuilder GroupBy(params Expr[] expressions) => Copy(groupBy: [.._groupBy, ..expressions]);

    /// <summary>Groups the result by the given columns.</summary>
    public SelectBuilder GroupBy(params IColumn[] columns)
        => Copy(groupBy: [.._groupBy, ..columns.Select(c => (Expr)c.ToRef())]);

    /// <summary>
    ///     Filters grouped rows. Repeated calls combine with <c>AND</c>, matching
    ///     <see cref="Where(Expr)"/>.
    /// </summary>
    public SelectBuilder Having(Expr expr)
        => Copy(having: _having is null ? expr : Sql.And(_having, expr));

    /// <summary>Appends a <c>UNION ALL</c> branch.</summary>
    public SelectBuilder UnionAll(SelectBuilder other) => UnionAll(other.Build());

    /// <summary>Appends a <c>UNION ALL</c> branch.</summary>
    public SelectBuilder UnionAll(SelectQuery other) => Copy(unionAll: [.._unionAll, other]);

    public SelectBuilder From(FromSource from) => Copy(from: from);

    public SelectBuilder From(ITable table) => From(table.ToFrom());

    public SelectBuilder Where(Expr expr)
        => Copy(where: _where is null ? expr : Sql.And(_where, expr));

    public SelectBuilder Where(IColumn column, object? value)
        => Where(new BinaryExpr(BinaryOp.Eq, column.ToRef(), column.Bind(value)));

    /// <summary>
    ///     Applies <paramref name="predicate"/> only when <paramref name="condition"/>
    ///     is true, without falling off the compiled path: the generator bakes the SQL
    ///     for each combination of conditionals and the interceptor picks one.
    /// </summary>
    /// <remarks>
    ///     The predicate must be statically visible, as anywhere else. Past
    ///     <c>MaxBakedConditionals</c> calls the combinations stop being worth baking
    ///     and the query falls back to runtime compilation.
    /// </remarks>
    public SelectBuilder WhereIf(bool condition, Expr predicate)
    {
        var index = _conditionalCount;
        var mask = condition ? ConditionalMask | (1UL << index) : ConditionalMask;
        var applied = condition ? Where(predicate) : this;
        return applied.Copy(conditionalCount: index + 1, conditionalMask: mask);
    }

    public SelectBuilder Where(params Expr[] conditions)
        => conditions.Length switch
        {
            0 => throw new ArgumentException("At least one condition is required.", nameof(conditions)),
            1 => Where(conditions[0]),
            _ => Where(Sql.And(conditions))
        };

    public SelectBuilder Timeout(TimeSpan timeout) => Copy(overlay: new QueryOptions(timeout));

    public SelectBuilder InnerJoin(FromSource target, Expr on)
        => Copy(joins: [.._joins, new JoinClause(JoinKind.Inner, target, on)]);

    public SelectBuilder InnerJoin(ITable target, Expr on) => InnerJoin(target.ToFrom(), on);

    public JoinBuilder InnerJoin(ITable target) => new(this, JoinKind.Inner, target.ToFrom());

    public SelectBuilder LeftJoin(FromSource target, Expr on)
        => Copy(joins: [.._joins, new JoinClause(JoinKind.Left, target, on)]);

    public SelectBuilder LeftJoin(ITable target, Expr on) => LeftJoin(target.ToFrom(), on);

    public JoinBuilder LeftJoin(ITable target) => new(this, JoinKind.Left, target.ToFrom());

    public SelectBuilder OrderBy(Expr expr)
        => Copy(orderBy: [.._orderBy, new OrderByItem(expr, false)]);

    public SelectBuilder OrderBy(IColumn column) => OrderBy(column.ToRef());

    public SelectBuilder OrderByDesc(Expr expr)
        => Copy(orderBy: [.._orderBy, new OrderByItem(expr, true)]);

    public SelectBuilder OrderByDesc(IColumn column) => OrderByDesc(column.ToRef());

    public SelectBuilder Distinct() => Copy(distinct: true);

    /// <summary>
    ///     Prefixes the query with a common table expression. A CTE whose body is a
    ///     statically visible chain is baked along with the outer query.
    /// </summary>
    public SelectBuilder With(CteClause cte) => Copy(with: [.._with, cte]);

    /// <summary>
    ///     Prefixes the query with a <c>WITH RECURSIVE</c> common table expression.
    /// </summary>
    public SelectBuilder WithRecursive(CteClause cte)
        => Copy(with: [.._with, cte], recursiveWith: true);

    public SelectBuilder Limit(int count) => Copy(limit: count);

    public SelectBuilder Offset(int count) => Copy(offset: count);

    public SelectBuilder Page(int page, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        return Copy(limit: pageSize, offset: (page - 1) * pageSize);
    }

    public SelectBuilder After(params (IColumn Column, object? Value)[] cursor)
    {
        if (_orderBy.Count == 0)
        {
            throw new InvalidOperationException("ORDER BY is required for After.");
        }

        if (cursor.Length != _orderBy.Count)
        {
            throw new ArgumentException("After requires one value per ORDER BY column.", nameof(cursor));
        }

        Expr? seek = null;
        Expr? equalityPrefix = null;
        for (var i = 0; i < cursor.Length; i++)
        {
            var column = cursor[i].Column.ToRef();
            var value = new ValueExpr(cursor[i].Value, cursor[i].Column.ClrType);
            var comparison = _orderBy[i].Descending
                ? new BinaryExpr(BinaryOp.Lt, column, value)
                : new BinaryExpr(BinaryOp.Gt, column, value);
            var term = equalityPrefix is null ? comparison : Sql.And(equalityPrefix, comparison);
            seek = seek is null ? term : Sql.Or(seek, term);
            var eq = new BinaryExpr(BinaryOp.Eq, column, value);
            equalityPrefix = equalityPrefix is null ? eq : Sql.And(equalityPrefix, eq);
        }

        return Copy(where: _where is null ? seek : Sql.And(_where, seek!));
    }

    public SelectQuery Build()
    {
        if (_from is null)
        {
            throw new InvalidOperationException("FROM is required.");
        }

        return new SelectQuery(
            _select,
            _from,
            _joins,
            _where,
            _orderBy,
            _limit,
            _offset,
            _distinct,
            _with,
            _recursiveWith,
            _unionAll,
            _groupBy.Count == 0 ? null : _groupBy,
            _having);
    }

    public Task<IReadOnlyList<T>> ToListAsync<T>(
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
        => Executor().QueryAsync(Build(), map, Overlay, cancellationToken);

    public async Task<T> FirstAsync<T>(
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync(map, cancellationToken);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("Sequence contains no elements.");
        }

        return rows[0];
    }

    public async Task<T> SingleAsync<T>(
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync(map, cancellationToken);
        return rows.Count switch
        {
            0 => throw new InvalidOperationException("Sequence contains no elements."),
            1 => rows[0],
            _ => throw new InvalidOperationException("Sequence contains more than one element.")
        };
    }

    public async Task<T?> FirstOrDefaultAsync<T>(
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync(map, cancellationToken);
        return rows.Count == 0 ? default : rows[0];
    }

    public async Task<T?> SingleOrDefaultAsync<T>(
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync(map, cancellationToken);
        return rows.Count switch
        {
            0 => default,
            1 => rows[0],
            _ => throw new InvalidOperationException("Sequence contains more than one element.")
        };
    }

    public IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
        => Executor().StreamAsync(Build(), map, Overlay, cancellationToken);

    // Delegate-free typed terminators. These runtime bodies are placeholders:
    // the source generator intercepts statically-visible call sites and routes
    // them through the precompiled path with a generated projection mapper.
    /// <summary>Runs the query and maps every row to <typeparamref name="T"/>.</summary>
    public Task<IReadOnlyList<T>> ToListAsync<T>(CancellationToken cancellationToken = default)
        => throw NotStaticallyVisible();

    /// <summary>Runs the query and returns the first row as <typeparamref name="T"/>.</summary>
    /// <exception cref="InvalidOperationException">No rows were returned.</exception>
    public Task<T> FirstAsync<T>(CancellationToken cancellationToken = default)
        => throw NotStaticallyVisible();

    /// <summary>Runs the query and returns the first row, or <c>default</c> if there are none.</summary>
    public Task<T?> FirstOrDefaultAsync<T>(CancellationToken cancellationToken = default)
        => throw NotStaticallyVisible();

    /// <summary>Runs the query and returns the only row.</summary>
    /// <exception cref="InvalidOperationException">Zero or more than one row was returned.</exception>
    public Task<T> SingleAsync<T>(CancellationToken cancellationToken = default)
        => throw NotStaticallyVisible();

    /// <summary>Runs the query and returns the only row, or <c>default</c> if there are none.</summary>
    /// <exception cref="InvalidOperationException">More than one row was returned.</exception>
    public Task<T?> SingleOrDefaultAsync<T>(CancellationToken cancellationToken = default)
        => throw NotStaticallyVisible();

    public Task<Page<T>> ToPageAsync<T>(
        bool includeTotal = false,
        CancellationToken cancellationToken = default)
        => throw NotStaticallyVisible();

    public Task<Page<T>> ToCursorPageAsync<T>(CancellationToken cancellationToken = default)
        => throw NotStaticallyVisible();

    private static InvalidOperationException NotStaticallyVisible()
        => new("Query shape is not statically visible. Use the delegate overload or restructure the chain.");

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Task<IReadOnlyList<T>> ToListPrecompiledAsync<T>(
        string sql,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
        => Executor().QueryPrecompiledAsync(sql, Build(), map, Overlay, cancellationToken);

    public Task<Page<T>> ToPageAsync<T>(
        Func<DbDataReader, T> map,
        bool includeTotal = false,
        CancellationToken cancellationToken = default)
        => ToPageCoreAsync(map, includeTotal, cancellationToken);

    public Task<Page<T>> ToCursorPageAsync<T>(
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
        => ToPageCoreAsync(map, includeTotal: false, cancellationToken);

    private async Task<Page<T>> ToPageCoreAsync<T>(
        Func<DbDataReader, T> map,
        bool includeTotal,
        CancellationToken cancellationToken)
    {
        var query = Build();
        if (query.Limit is not int pageSize)
        {
            throw new InvalidOperationException("Page size is required. Call Page or Limit first.");
        }

        var fetch = query with { Limit = pageSize + 1, WindowCount = includeTotal };
        int? total = null;
        var rows = await Executor().QueryAsync(
            fetch,
            reader =>
            {
                if (includeTotal)
                {
                    total ??= reader.GetInt32(reader.FieldCount - 1);
                }

                return map(reader);
            },
            Overlay,
            cancellationToken);

        var hasMore = rows.Count > pageSize;
        IReadOnlyList<T> items = hasMore ? [..rows.Take(pageSize)] : rows;
        return new Page<T>(items, hasMore, total);
    }

    private IQueryExecutor Executor()
        => _executor ?? throw new InvalidOperationException("This query is not bound to a database.");

    private SelectBuilder Copy(
        EquatableList<SelectItem>? select = null,
        FromSource? from = null,
        EquatableList<JoinClause>? joins = null,
        Expr? where = null,
        EquatableList<OrderByItem>? orderBy = null,
        int? limit = null,
        int? offset = null,
        bool? distinct = null,
        EquatableList<CteClause>? with = null,
        bool? recursiveWith = null,
        EquatableList<SelectQuery>? unionAll = null,
        EquatableList<Expr>? groupBy = null,
        Expr? having = null,
        int? conditionalCount = null,
        ulong? conditionalMask = null,
        QueryOptions? overlay = null)
        => new(
            _executor,
            overlay ?? Overlay,
            select ?? _select,
            from ?? _from,
            joins ?? _joins,
            where ?? _where,
            orderBy ?? _orderBy,
            limit ?? _limit,
            offset ?? _offset,
            distinct ?? _distinct,
            with ?? _with,
            recursiveWith ?? _recursiveWith,
            unionAll ?? _unionAll,
            groupBy ?? _groupBy,
            having ?? _having,
            conditionalCount ?? _conditionalCount,
            conditionalMask ?? ConditionalMask);
}
