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
    private readonly SelectQuery? _fromSelect;
    private readonly EquatableList<SelectItem> _returning;
    private readonly EquatableList<RuntimeProjectionColumn> _returningColumns;
    private readonly EquatableList<CteClause> _with;
    private readonly bool _recursiveWith;

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
        _fromSelect = fromSelect;
        _returning = returning;
        _returningColumns = returningColumns;
        _with = with;
        _recursiveWith = recursiveWith;
    }

    public QueryOptions? Overlay { get; }

    public InsertBuilder Value(IColumn column, object? value)
    {
        if (_fromSelect is not null)
        {
            throw new InvalidOperationException("Insert requires exactly one of VALUES or a source select.");
        }

        return Copy(
            currentRow: [.._currentRow, column.Bind(value)],
            currentColumns: [.._currentColumns, column.Name]);
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
            rows: [.._rows, _currentRow],
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
            columns: [..columns.Select(c => c.Name)],
            fromSelect: source);
    }

    public InsertBuilder Returning(params IColumn[] columns)
        => Copy(
            returning: [..columns.Select(c => new SelectItem(c.ToRef(), c.ProjectionName))],
            returningColumns: [..columns.Select(RuntimeProjectionColumn.From)]);

    public InsertBuilder With(CteClause cte) => Copy(with: [.._with, cte]);

    public InsertBuilder WithRecursive(CteClause cte)
        => Copy(with: [.._with, cte], recursiveWith: true);

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

            rows = [..rows, _currentRow];
        }

        foreach (var row in rows)
        {
            if (row.Count != columns.Count)
            {
                throw new InvalidOperationException("All rows must set the same columns.");
            }
        }

        if (rows.Count > 0 == _fromSelect is not null)
        {
            throw new InvalidOperationException("Insert requires exactly one of VALUES or a source select.");
        }

        return new InsertQuery(_table.ToFrom(), columns, rows, _fromSelect, _returning, _with, _recursiveWith);
    }

    public Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
        => Executor().ExecuteAsync(Build(), Overlay, cancellationToken);

    public Task<IReadOnlyList<T>> ToListAsync<T>(
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
        => Executor().QueryAsync(Build(), map, Overlay, cancellationToken);

    public Task<IReadOnlyList<T>> ToListAsync<T>(CancellationToken cancellationToken = default)
    {
        EnsureReturningProjection();
        return ToListAsync(reader => RuntimeProjectionMapper.Read<T>(_returningColumns, reader), cancellationToken);
    }

    public async Task<T> FirstAsync<T>(CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync<T>(cancellationToken);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("Sequence contains no elements.");
        }

        return rows[0];
    }

    public async Task<T?> FirstOrDefaultAsync<T>(CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync<T>(cancellationToken);
        return rows.Count == 0 ? default : rows[0];
    }

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
            fromSelect ?? _fromSelect,
            returning ?? _returning,
            returningColumns ?? _returningColumns,
            with ?? _with,
            recursiveWith ?? _recursiveWith);
}
