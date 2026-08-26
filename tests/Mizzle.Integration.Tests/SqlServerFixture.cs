using Testcontainers.MsSql;

namespace Mizzle.Integration.Tests;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();

    public SqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        DataSource = SqlDataSource.Create(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (DataSource is not null)
        {
            await DataSource.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}
