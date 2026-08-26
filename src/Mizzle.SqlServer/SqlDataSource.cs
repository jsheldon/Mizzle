using Microsoft.Data.SqlClient;

namespace Mizzle.SqlServer;

public sealed class SqlDataSource : IAsyncDisposable
{
    public SqlDataSource(string connectionString)
    {
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static SqlDataSource Create(string connectionString) => new(connectionString);

    public async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
