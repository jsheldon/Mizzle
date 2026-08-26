using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Mizzle.SqlServer;

public static class MizzleSqlServerServiceCollectionExtensions
{
    public static IServiceCollection AddMizzleSqlServer(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(_ => SqlDataSource.Create(connectionString));
        services.AddSingleton(sp => new SqlDb(sp.GetRequiredService<SqlDataSource>()));
        return services;
    }

    public static IServiceCollection AddMizzleSqlServer(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionStringFactory)
    {
        services.AddSingleton<SqlDataSourceCache>();
        services.AddScoped(sp =>
        {
            var connectionString = connectionStringFactory(sp);
            return new SqlDb(sp.GetRequiredService<SqlDataSourceCache>().Get(connectionString));
        });
        return services;
    }

    public static IServiceCollection AddMizzleSqlServer(this IServiceCollection services, SqlDataSource dataSource)
    {
        services.AddSingleton(dataSource);
        services.AddSingleton(_ => new SqlDb(dataSource));
        return services;
    }
}

internal sealed class SqlDataSourceCache
{
    private readonly ConcurrentDictionary<string, SqlDataSource> _cache = new();

    public SqlDataSource Get(string connectionString)
        => _cache.GetOrAdd(connectionString, static cs => SqlDataSource.Create(cs));
}
