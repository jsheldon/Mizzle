namespace Mizzle.Schema;

public abstract record TableConstraint;

public sealed record CompositePrimaryKey(IReadOnlyList<IColumn> Columns) : TableConstraint;

