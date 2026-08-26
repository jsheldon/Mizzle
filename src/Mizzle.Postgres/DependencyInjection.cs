using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Mizzle.Postgres;

public static class MizzlePostgresServiceCollectionExtensions
{
    public static IServiceCollection AddMizzlePostgres(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton(sp => new PostgresDb(sp.GetRequiredService<NpgsqlDataSource>()));
        return services;
    }

    public static IServiceCollection AddMizzlePostgres(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionStringFactory)
    {
        services.AddSingleton<NpgsqlDataSourceCache>();
        services.AddScoped(sp =>
        {
            var connectionString = connectionStringFactory(sp);
            return new PostgresDb(sp.GetRequiredService<NpgsqlDataSourceCache>().Get(connectionString));
        });
        return services;
    }

    public static IServiceCollection AddMizzlePostgres(this IServiceCollection services, NpgsqlDataSource dataSource)
    {
        services.AddSingleton(dataSource);
        services.AddSingleton(_ => new PostgresDb(dataSource));
        return services;
    }
}

internal sealed class NpgsqlDataSourceCache
{
    private readonly ConcurrentDictionary<string, NpgsqlDataSource> _cache = new();

    public NpgsqlDataSource Get(string connectionString)
        => _cache.GetOrAdd(connectionString, static cs => NpgsqlDataSource.Create(cs));
}
