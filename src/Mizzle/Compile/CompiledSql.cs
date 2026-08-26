namespace Mizzle.Compile;

public sealed record CompiledSql(string Sql, IReadOnlyList<object?> Parameters);
