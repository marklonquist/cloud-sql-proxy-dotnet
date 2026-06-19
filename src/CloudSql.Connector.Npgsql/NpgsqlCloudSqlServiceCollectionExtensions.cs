using CloudSql.Connector;
using CloudSql.Connector.Npgsql;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

// ReSharper disable once CheckNamespace -- discoverable from the standard DI namespace.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration helpers for a Cloud SQL-backed <see cref="NpgsqlDataSource"/>.
/// </summary>
public static class NpgsqlCloudSqlServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="NpgsqlDataSource"/> (and <see cref="NpgsqlConnection"/>
    /// as a transient) backed by the in-process Cloud SQL connector. Calls
    /// <see cref="CloudSqlConnectorServiceCollectionExtensions.AddCloudSqlConnector"/> if the
    /// connector has not already been registered.
    /// </summary>
    public static IServiceCollection AddCloudSqlNpgsqlDataSource(
        this IServiceCollection services,
        string instanceConnectionName,
        string baseConnectionString,
        IpType? ipType = null,
        bool useIamAuthentication = false,
        Action<NpgsqlDataSourceBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCloudSqlConnector();

        services.TryAddSingleton(sp =>
        {
            var connector = sp.GetRequiredService<CloudSqlConnector>();
            // StartLocalProxyAsync completes synchronously (the listener binds eagerly), so this
            // does not block on real I/O.
            return connector
                .CreateCloudSqlDataSourceAsync(
                    instanceConnectionName,
                    baseConnectionString,
                    ipType,
                    useIamAuthentication,
                    configure)
                .GetAwaiter()
                .GetResult();
        });

        services.TryAddTransient(sp => sp.GetRequiredService<NpgsqlDataSource>().CreateConnection());

        return services;
    }
}
