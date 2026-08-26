using System.Reflection;
using Mizzle.Ir;

namespace Mizzle.Schema;

public abstract class Table<TSelf> : ITable
    where TSelf : Table<TSelf>
{
    protected Table(string name, string? schema = null, string? alias = null)
    {
        Name = name;
        Schema = schema;
        Alias = alias ?? name;
        BindColumns();
        Constraints = [..DefineConstraints()];
    }

    public string Name { get; }
    public string? Schema { get; }
    public abstract DialectKind Dialect { get; }
    public string Alias { get; }

    public IReadOnlyList<IColumn> Columns { get; private set; } = [];

    public IReadOnlyList<TableConstraint> Constraints { get; }

    public FromSource ToFrom() => new(Name, Schema, Alias);

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
