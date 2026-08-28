using System.Reflection;
using Mizzle.Ir;

namespace Mizzle.Schema;

/// <summary>
///     Base class for a table definition. Derive with the table type as
///     <typeparamref name="TSelf"/> and declare columns as get-only properties.
/// </summary>
/// <typeparam name="TSelf">The deriving table type.</typeparam>
public abstract class Table<TSelf> : ITable
    where TSelf : Table<TSelf>
{
    protected Table(string name, string? schema = null)
    {
        Name = name;
        Schema = schema;
        Alias = name;
        BindColumns();
        Constraints = [..DefineConstraints()];
    }

    /// <summary>The table's name in the database.</summary>
    public string Name { get; }
    /// <summary>The schema the table lives in, or null for the connection's default.</summary>
    public string? Schema { get; }
    public abstract DialectKind Dialect { get; }
    /// <summary>
    ///     The alias this instance uses in emitted SQL. Defaults to <see cref="Name"/>;
    ///     change it with <see cref="WithAlias"/>.
    /// </summary>
    public string Alias { get; private set; }

    public IReadOnlyList<IColumn> Columns { get; private set; } = [];

    public IReadOnlyList<TableConstraint> Constraints { get; }

    public FromSource ToFrom() => new(Name, Schema, Alias);

    /// <summary>
    ///     A second instance of this table under a different alias, so one query can
    ///     join it more than once -- a lookup table joined for several coded fields,
    ///     or a self-join.
    /// </summary>
    /// <param name="alias">The alias to use in SQL, e.g. <c>"lang"</c>.</param>
    /// <returns>A new instance. The original keeps its alias and stays shareable.</returns>
    /// <exception cref="InvalidOperationException">
    ///     <typeparamref name="TSelf"/> has no parameterless constructor. The analyzer
    ///     reports this as <c>MIZ012</c> at the call site.
    /// </exception>
    /// <remarks>
    ///     Two tables sharing an alias in one query is <c>MIZ011</c>.
    /// </remarks>
    public TSelf WithAlias(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException("Alias must be a non-empty string.", nameof(alias));
        }

        TSelf copy;
        try
        {
            copy = (TSelf)Activator.CreateInstance(typeof(TSelf), nonPublic: true)!;
        }
        catch (MissingMethodException e)
        {
            throw new InvalidOperationException(
                $"{typeof(TSelf).Name} needs a parameterless constructor for WithAlias.", e);
        }

        copy.Alias = alias;
        copy.BindColumns();
        return copy;
    }

    protected virtual IEnumerable<TableConstraint> DefineConstraints() => [];

    private void BindColumns()
    {
        var columns = new List<IColumn>();
        foreach (var property in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = property.GetValue(this);
            if (value is IBindableColumn bindable)
            {
                bindable.Bind(Alias);
            }

            if (value is IColumn column)
            {
                columns.Add(column);
            }
        }

        Columns = columns;
    }
}
