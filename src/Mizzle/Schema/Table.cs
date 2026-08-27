using System.Reflection;
using Mizzle.Ir;

namespace Mizzle.Schema;

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

    public string Name { get; }
    public string? Schema { get; }
    public abstract DialectKind Dialect { get; }
    public string Alias { get; private set; }

    public IReadOnlyList<IColumn> Columns { get; private set; } = [];

    public IReadOnlyList<TableConstraint> Constraints { get; }

    public FromSource ToFrom() => new(Name, Schema, Alias);

    // A second instance of the same table under a different alias, so one query
    // can join it more than once (lookup tables, self-joins). Returns a new
    // instance. The original keeps its default alias and stays shareable.
    // Requires TSelf to have a parameterless constructor.
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
