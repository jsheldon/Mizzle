using Mizzle.Ir;

namespace Mizzle.Schema;

public interface ITable
{
    string Name { get; }
    string? Schema { get; }
    DialectKind Dialect { get; }
    string Alias { get; }
    IReadOnlyList<IColumn> Columns { get; }
    FromSource ToFrom();
}
