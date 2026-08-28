namespace Mizzle.Cli.Schema;

internal sealed record TableInfo(string Schema, string Name, IReadOnlyList<ColumnInfo> Columns);
