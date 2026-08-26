using Microsoft.Extensions.DependencyInjection;
using Mizzle.Postgres;

namespace Mizzle.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddMizzlePostgres_registers_singleton_db()
    {
        var services = new ServiceCollection();
        services.AddMizzlePostgres("Host=localhost;Username=x;Password=y;Database=z");
        using var sp = services.BuildServiceProvider();
        var a = sp.GetRequiredService<PostgresDb>();
        var b = sp.GetRequiredService<PostgresDb>();
        Assert.Same(a, b);
    }

    [Fact]
    public void AddMizzlePostgres_factory_reuses_datasource_for_same_connection_string()
    {
        var services = new ServiceCollection();
        services.AddMizzlePostgres(_ => "Host=localhost;Username=x;Password=y;Database=z");
        using var sp = services.BuildServiceProvider();
        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();
        var a = scope1.ServiceProvider.GetRequiredService<PostgresDb>();
        var b = scope2.ServiceProvider.GetRequiredService<PostgresDb>();
        Assert.NotSame(a, b);
    }
}
