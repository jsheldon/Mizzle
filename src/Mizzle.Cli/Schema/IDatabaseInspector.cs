namespace Mizzle.Cli.Schema;

internal interface IDatabaseInspector
{
    Task<IReadOnlyList<TableInfo>> InspectAsync(string connectionString, string? schema, IReadOnlyList<string>? tables, CancellationToken cancellationToken);
}
