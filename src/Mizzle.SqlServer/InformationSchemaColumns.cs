namespace Mizzle.SqlServer;

// The ANSI-standard information_schema.columns view -- identical shape on SQL
// Server and Postgres, backing SqlDb.ColumnExistsAsync/PostgresDb.ColumnExistsAsync.
internal sealed class InformationSchemaColumns : SqlTable<InformationSchemaColumns>
{
    public InformationSchemaColumns() : base("columns", "information_schema")
    {
    }

    public SqlColumn<string> TableSchema { get; } = VarChar("table_schema", 128);
    public SqlColumn<string> TableName { get; } = VarChar("table_name", 128);
    public SqlColumn<string> ColumnName { get; } = VarChar("column_name", 128);
}
