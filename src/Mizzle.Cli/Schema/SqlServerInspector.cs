using Microsoft.Data.SqlClient;

namespace Mizzle.Cli.Schema;

internal sealed class SqlServerInspector : IDatabaseInspector
{
    public async Task<IReadOnlyList<TableInfo>> InspectAsync(string connectionString, string? schema, IReadOnlyList<string>? tables, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                s.name AS schema_name,
                t.name AS table_name,
                c.name AS column_name,
                ty.name AS store_type,
                ty.name AS native_type,
                CASE WHEN c.max_length < 0 THEN NULL WHEN ty.name IN ('nvarchar', 'nchar') THEN c.max_length / 2 ELSE c.max_length END AS length,
                c.is_nullable,
                CONVERT(bit, CASE WHEN pk.column_id IS NULL THEN 0 ELSE 1 END) AS is_primary_key,
                c.is_identity
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.columns c ON c.object_id = t.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN (
                SELECT ic.object_id, ic.column_id
                FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                WHERE i.is_primary_key = 1
            ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
            WHERE (@schema IS NULL OR s.name = @schema)
            ORDER BY s.name, t.name, c.column_id
            """;

        var filter = tables is { Count: > 0 } ? new HashSet<string>(tables, StringComparer.OrdinalIgnoreCase) : null;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", (object?)schema ?? DBNull.Value);

        var columns = new List<ColumnInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var table = reader.GetString(1);
            if (filter is not null && !filter.Contains(table))
            {
                continue;
            }

            columns.Add(new ColumnInfo(
                reader.GetString(0),
                table,
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8)));
        }

        return [.. columns.GroupBy(c => (c.Schema, c.Table)).Select(g => new TableInfo(g.Key.Schema, g.Key.Table, [.. g]))];
    }
}
