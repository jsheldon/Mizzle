using System.Data.Common;
using Mizzle.Ir;
using Mizzle.Schema;

namespace Mizzle.Fluent;

public sealed class InsertBuilder
{
    private readonly ITable _table;
    private readonly IQueryExecutor? _executor;
    private readonly EquatableList<string> _columns;
    private readonly EquatableList<EquatableList<Expr>> _rows;
    private readonly EquatableList<Expr> _currentRow;
    private readonly EquatableList<string> _currentColumns;
    private readonly SelectQuery? _sourceQuery;
    private readonly EquatableList<SelectItem> _returningItems;
    private readonly EquatableList<RuntimeProjectionColumn> _returningColumns;
    private readonly EquatableList<CteClause> _commonTableExpressions;
    private readonly bool _hasRecursiveCte;

    public InsertBuilder(ITable table, IQueryExecutor? executor = null, QueryOptions? overlay = null)
        : this(table, executor, overlay, [], [], [], [], null, [], [], [], false)
    {
    }

    private InsertBuilder(
        ITable table,
        IQueryExecutor? executor,
        QueryOptions? overlay,
        EquatableList<string> columns,
        EquatableList<EquatableList<Expr>> rows,
        EquatableList<Expr> currentRow,
        EquatableList<string> currentColumns,
        SelectQuery? fromSelect,
        EquatableList<SelectItem> returning,
        EquatableList<RuntimeProjectionColumn> returningColumns,
        EquatableList<CteClause> with,
        bool recursiveWith)
    {
        _table = table;
        _executor = executor;
        Overlay = overlay;
        _columns = columns;
        _rows = rows;
        _currentRow = currentRow;
        _currentColumns = currentColumns;
        _sourceQuery = fromSelect;
        _returningItems = returning;
        _returningColumns = returningColumns;
        _commonTableExpressions = with;
        _hasRecursiveCte = recursiveWith;
    }

    public QueryOptions? Overlay { get; }

    // Generic over the column's own type, matching Column<T>.Eq(T): a mismatched
    // value no longer compiles instead of failing only at the database.
    public InsertBuilder Value<T>(Column<T> column, T value)
    {
        if (_sourceQuery is not null)
        {
            throw new InvalidOperationException("Insert requires exactly one of VALUES or a source select.");
        }

        return Copy(
            currentRow: [.. _currentRow, column.Bind(value)],
            currentColumns: [.. _currentColumns, column.Name]);
    }

    /// <summary>
    ///     Binds a computed/server-side expression (e.g. <c>TSql.GetDate()</c>) as this column's
    ///     value. Emitted as literal SQL in the VALUES list, not a bound parameter -- use this for
    ///     values the database itself must compute (server clocks, sequences, etc.) that an
    ///     application-supplied literal cannot faithfully substitute for. Prefer the
    ///     <see cref="Value{T}(Column{T},T)"/> overload for ordinary literal values.
    /// </summary>
    public InsertBuilder Value<T>(Column<T> column, Expr expression)
    {
        if (_sourceQuery is not null)
        {
            throw new InvalidOperationException("Insert requires exactly one of VALUES or a source select.");
        }

        return Copy(
            currentRow: [.. _currentRow, expression],
            currentColumns: [.. _currentColumns, column.Name]);
    }

    public InsertBuilder NewRow()
    {
        if (_currentRow.Count == 0)
        {
            throw new InvalidOperationException("Set at least one value before starting the next row.");
        }

        if (_columns.Count > 0 && !_columns.Equals(_currentColumns))
        {
            throw new InvalidOperationException("All rows must set the same columns.");
        }

        return Copy(
            columns: _columns.Count == 0 ? _currentColumns : _columns,
            rows: [.. _rows, _currentRow],
            currentRow: new EquatableList<Expr>([]),
            currentColumns: new EquatableList<string>([]),
            resetCurrent: true);
    }

    public InsertBuilder Select(SelectQuery source, params IColumn[] columns)
    {
        if (_currentRow.Count > 0 || _rows.Count > 0)
        {
            throw new InvalidOperationException("Insert requires exactly one of VALUES or a source select.");
        }

        return Copy(
            columns: [.. columns.Select(c => c.Name)],
            fromSelect: source);
    }

    /// <summary>
    ///     The columns to return from the affected rows, read back through the typed
    ///     terminators. <c>As(...)</c> works here as it does in a select list.
    /// </summary>
    public InsertBuilder Returning(params IColumn[] columns)
        => Copy(
            returning: [.. columns.Select(c => new SelectItem(c.ToRef(), c.ProjectionName))],
            returningColumns: [.. columns.Select(RuntimeProjectionColumn.From)]);

    /// <summary>Prefixes the statement with a common table expression.</summary>
    public InsertBuilder With(CteClause cte) => Copy(with: [.. _commonTableExpressions, cte]);

    /// <summary>Prefixes the statement with a <c>WITH RECURSIVE</c> common table expression.</summary>
    public InsertBuilder WithRecursive(CteClause cte)
        => Copy(with: [.. _commonTableExpressions, cte], recursiveWith: true);

    public InsertBuilder Timeout(TimeSpan timeout) => Copy(overlay: new QueryOptions(timeout));

    public InsertQuery Build()
    {
        var rows = _rows;
        var columns = _columns;
        if (_currentRow.Count > 0)
        {
            if (columns.Count == 0)
            {
                columns = _currentColumns;
            }
            else if (!columns.Equals(_currentColumns))
            {
                throw new InvalidOperationException("All rows must set the same columns.");
            }

            rows = [.. rows, _currentRow];
        }

        foreach (var row in rows)
        {
            if (row.Count != columns.Count)
            {
                throw new InvalidOperationException("All rows must set the same columns.");
            }
        }

        if (rows.Count > 0 == _sourceQuery is not null)
        {
            throw new InvalidOperationException("Insert requires exactly one of VALUES or a source select.");
        }

        return new InsertQuery(_table.ToFrom(), columns, rows, _sourceQuery, _returningItems, _commonTableExpressions, _hasRecursiveCte);
    }

    public Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
        => Executor().ExecuteAsync(Build(), Overlay, cancellationToken);

    public Task<IReadOnlyList<T>> ToListAsync<T>(
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
        => Executor().QueryAsync(Build(), map, Overlay, cancellationToken);

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
            throw new InvalidOperationException("Typed insert projection requires Returning(...).");
        }
    }

    private InsertBuilder Copy(
        EquatableList<string>? columns = null,
        EquatableList<EquatableList<Expr>>? rows = null,
        EquatableList<Expr>? currentRow = null,
        EquatableList<string>? currentColumns = null,
        SelectQuery? fromSelect = null,
        EquatableList<SelectItem>? returning = null,
        EquatableList<RuntimeProjectionColumn>? returningColumns = null,
        EquatableList<CteClause>? with = null,
        bool? recursiveWith = null,
        QueryOptions? overlay = null,
        bool resetCurrent = false)
        => new(
            _table,
            _executor,
            overlay ?? Overlay,
            columns ?? _columns,
            rows ?? _rows,
            resetCurrent ? new EquatableList<Expr>([]) : currentRow ?? _currentRow,
            resetCurrent ? new EquatableList<string>([]) : currentColumns ?? _currentColumns,
            fromSelect ?? _sourceQuery,
            returning ?? _returningItems,
            returningColumns ?? _returningColumns,
            with ?? _commonTableExpressions,
            recursiveWith ?? _hasRecursiveCte);
}
