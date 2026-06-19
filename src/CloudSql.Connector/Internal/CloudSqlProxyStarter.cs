using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudSql.Connector.Internal;

/// <summary>
/// Starts a proxy for every instance listed in <see cref="ConnectorOptions.Instances"/> when the
/// host starts, binding each to its resolved local endpoint (a TCP port or a Unix domain socket)
/// before the application begins serving requests. Port, address and socket selection mirror the
/// Cloud SQL Auth Proxy binary. Resolving the same instance later via
/// <see cref="CloudSqlConnector.StartLocalProxyAsync"/> returns the already-started proxy.
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
        // Parse everything up front so a malformed entry fails before any listener is bound.
        var configs = _options.Instances.Select(ProxyInstanceConfig.Parse).ToList();
        var assigner = new PortAssigner(_options.Port);

        foreach (var config in configs)
        {
            var bindEndpoint = await ResolveBindEndpointAsync(config, assigner, cancellationToken)
                .ConfigureAwait(false);

            // GetOrStartProxy binds the listener synchronously; it does not dial the instance
            // until a client connects.
            var proxy = _connector.GetOrStartProxy(config.Instance, _options.DefaultIpType, bindEndpoint);

            _logger.LogInformation(
                "Cloud SQL proxy for {Instance} listening on {Endpoint}.",
                config.Instance.Original, proxy.LocalEndPoint);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<EndPoint> ResolveBindEndpointAsync(
        ProxyInstanceConfig config,
        PortAssigner assigner,
        CancellationToken cancellationToken)
    {
        // A Unix domain socket takes precedence over TCP, matching the binary.
        if (config.UnixSocketPath is not null)
        {
            return new UnixDomainSocketEndPoint(config.UnixSocketPath);
        }

        var unixSocketDir = config.UnixSocketDir ?? _options.UnixSocketPath;
        if (unixSocketDir is not null)
        {
            var path = await ResolveUnixSocketPathAsync(config, unixSocketDir, cancellationToken)
                .ConfigureAwait(false);
            return new UnixDomainSocketEndPoint(path);
        }

        int port;
        if (config.Port.HasValue)
        {
            port = config.Port.Value;
        }
        else if (assigner.UsesGlobalPort)
        {
            port = assigner.NextGlobalPort();
        }
        else
        {
            var version = await _connector
                .GetEngineVersionAsync(config.Instance, cancellationToken)
                .ConfigureAwait(false);
            port = assigner.NextEnginePort(version);
        }

        var address = ParseAddress(config.Address ?? _options.Address, config.Instance.Original);
        return new IPEndPoint(address, port);
    }

    private async Task<string> ResolveUnixSocketPathAsync(
        ProxyInstanceConfig config,
        string directory,
        CancellationToken cancellationToken)
    {
        var basePath = Path.Combine(directory, config.Instance.Original);

        var version = await _connector
            .GetEngineVersionAsync(config.Instance, cancellationToken)
            .ConfigureAwait(false);

        // PostgreSQL clients expect a directory containing a '.s.PGSQL.5432' socket file.
        return version.StartsWith("POSTGRES", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(basePath, ".s.PGSQL.5432")
            : basePath;
    }

    private static IPAddress ParseAddress(string address, string instance)
    {
        if (string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Loopback;
        }

        if (IPAddress.TryParse(address, out var parsed))
        {
            return parsed;
        }

        throw new FormatException(
            $"Invalid address '{address}' for instance '{instance}'. Expected an IP address " +
            "(for example 127.0.0.1 or 0.0.0.0) or 'localhost'.");
    }
}
