using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudSql.Connector.Internal;

/// <summary>
/// Starts an in-process loopback proxy for every instance listed in
/// <see cref="ConnectorOptions.Instances"/> when the host starts, so the listeners are bound
/// and ready before the application begins serving requests. Resolving the same instance later
/// via <see cref="CloudSqlConnector.StartLocalProxyAsync"/> returns the already-started proxy.
/// </summary>
internal sealed class CloudSqlProxyStarter : IHostedService
{
    private readonly CloudSqlConnector _connector;
    private readonly ConnectorOptions _options;
    private readonly ILogger<CloudSqlProxyStarter> _logger;

    public CloudSqlProxyStarter(
        CloudSqlConnector connector,
        IOptions<ConnectorOptions> options,
        ILogger<CloudSqlProxyStarter> logger)
    {
        _connector = connector;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var instance in _options.Instances)
        {
            // StartLocalProxyAsync binds the loopback listener synchronously; it does not dial
            // the instance until a client connects, so startup never blocks on network I/O.
            var endpoint = await _connector
                .StartLocalProxyAsync(instance, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Cloud SQL loopback proxy for {Instance} listening on {Endpoint}.",
                instance, endpoint);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
