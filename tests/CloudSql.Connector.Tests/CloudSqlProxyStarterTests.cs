using System.Net;
using System.Net.Sockets;
using CloudSql.Connector;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CloudSql.Connector.Tests;

public class CloudSqlProxyStarterTests
{
    [Fact]
    public async Task ExplicitPerInstancePorts_BindThoseExactPorts()
    {
        var portA = GetFreePort();
        var portB = GetFreePort();

        await using var provider = BuildProvider(options =>
        {
            options.Instances.Add($"proj:europe-west1:db-a?port={portA}");
            options.Instances.Add($"proj:europe-west1:db-b?port={portB}");
        });

        await StartHostedServicesAsync(provider);

        var connector = provider.GetRequiredService<CloudSqlConnector>();
        var endpointA = await connector.StartLocalProxyAsync("proj:europe-west1:db-a");
        var endpointB = await connector.StartLocalProxyAsync("proj:europe-west1:db-b");

        Assert.Equal(IPAddress.Loopback, endpointA.Address);
        Assert.Equal(portA, endpointA.Port);
        Assert.Equal(portB, endpointB.Port);
    }

    [Fact]
    public async Task GlobalPort_IncrementsAcrossInstances()
    {
        // No explicit per-instance ports, so a configured global base port avoids any metadata
        // (network) lookup while still exercising the increment path.
        var basePort = GetFreePort();

        await using var provider = BuildProvider(options =>
        {
            options.Port = basePort;
            options.Instances.Add("proj:europe-west1:db-a");
            options.Instances.Add("proj:europe-west1:db-b");
        });

        await StartHostedServicesAsync(provider);

        var connector = provider.GetRequiredService<CloudSqlConnector>();
        var endpointA = await connector.StartLocalProxyAsync("proj:europe-west1:db-a");
        var endpointB = await connector.StartLocalProxyAsync("proj:europe-west1:db-b");

        Assert.Equal(basePort, endpointA.Port);
        Assert.Equal(basePort + 1, endpointB.Port);
    }

    [Fact]
    public async Task NoConfiguredInstances_StartsCleanly()
    {
        await using var provider = BuildProvider(_ => { });

        await StartHostedServicesAsync(provider);
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StopAsync(CancellationToken.None);
        }
    }

    private static ServiceProvider BuildProvider(Action<ConnectorOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCloudSqlConnector(configure);
        return services.BuildServiceProvider();
    }

    private static async Task StartHostedServicesAsync(IServiceProvider provider)
    {
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
