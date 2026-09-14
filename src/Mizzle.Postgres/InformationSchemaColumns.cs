namespace Mizzle.Postgres;

// The ANSI-standard information_schema.columns view -- identical shape on SQL
// Server and Postgres, backing SqlDb.ColumnExistsAsync/PostgresDb.ColumnExistsAsync.
internal sealed class InformationSchemaColumns : PgTable<InformationSchemaColumns>
{
    public InformationSchemaColumns() : base("columns", "information_schema")
    {
    }

    public PgColumn<string> TableSchema { get; } = Text("table_schema");
    public PgColumn<string> TableName { get; } = Text("table_name");
    public PgColumn<string> ColumnName { get; } = Text("column_name");
}
