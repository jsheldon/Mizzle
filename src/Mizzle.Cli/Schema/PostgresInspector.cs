using Npgsql;

namespace Mizzle.Cli.Schema;

internal sealed class PostgresInspector : IDatabaseInspector
{
    public async Task<IReadOnlyList<TableInfo>> InspectAsync(string connectionString, string? schema, IReadOnlyList<string>? tables, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                c.table_schema,
                c.table_name,
                c.column_name,
                c.data_type,
                c.udt_name,
                c.character_maximum_length,
                c.is_nullable = 'YES' AS is_nullable,
                COALESCE(pk.is_pk, false) AS is_primary_key,
                c.is_identity = 'YES' AS is_identity
            FROM information_schema.columns c
            LEFT JOIN (
                SELECT kcu.table_schema, kcu.table_name, kcu.column_name, true AS is_pk
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                  ON tc.constraint_schema = kcu.constraint_schema
                 AND tc.constraint_name = kcu.constraint_name
                WHERE tc.constraint_type = 'PRIMARY KEY'
            ) pk
              ON pk.table_schema = c.table_schema
             AND pk.table_name = c.table_name
             AND pk.column_name = c.column_name
            WHERE c.table_schema = COALESCE(@schema, c.table_schema)
              AND c.table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY c.table_schema, c.table_name, c.ordinal_position
            """;

        var filter = tables is { Count: > 0 } ? new HashSet<string>(tables, StringComparer.OrdinalIgnoreCase) : null;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", (object?)schema ?? DBNull.Value);

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
