using CloudSql.Connector;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// first use, so registration never blocks.
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

        return services;
    }
}
