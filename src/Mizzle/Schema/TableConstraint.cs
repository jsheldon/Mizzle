namespace Mizzle.Schema;

public abstract record TableConstraint;

public sealed record CompositePrimaryKey(IReadOnlyList<IColumn> Columns) : TableConstraint;

public sealed record CompositeUnique(IReadOnlyList<IColumn> Columns) : TableConstraint;
