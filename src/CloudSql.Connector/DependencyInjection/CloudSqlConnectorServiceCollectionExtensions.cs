using CloudSql.Connector;
using CloudSql.Connector.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ReSharper disable once CheckNamespace -- discoverable from the standard DI namespace.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration helpers for <see cref="CloudSqlConnector"/>.
/// </summary>
public static class CloudSqlConnectorServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="CloudSqlConnector"/>. Credentials are resolved lazily on
    /// first use, so registration never blocks. Any instance names configured in
    /// <see cref="ConnectorOptions.Instances"/> have a loopback proxy started for them
    /// automatically when the host starts.
    /// </summary>
    public static IServiceCollection AddCloudSqlConnector(
        this IServiceCollection services,
        Action<ConnectorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<ConnectorOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddSingleton(sp => CloudSqlConnector.Create(
            sp.GetRequiredService<IOptions<ConnectorOptions>>().Value,
            sp.GetService<ILoggerFactory>()));

        // Eagerly start a loopback proxy for any instance listed in ConnectorOptions.Instances.
        // AddHostedService dedupes by implementation type, so repeated registration is harmless.
        services.AddHostedService<CloudSqlProxyStarter>();

        return services;
    }
}
