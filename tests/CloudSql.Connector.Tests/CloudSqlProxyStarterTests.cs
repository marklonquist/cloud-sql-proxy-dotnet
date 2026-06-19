using CloudSql.Connector;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CloudSql.Connector.Tests;

public class CloudSqlProxyStarterTests
{
    [Fact]
    public async Task ConfiguredInstances_StartProxiesAtHostStartup()
    {
        var instances = new[]
        {
            "proj:europe-west1:db-a",
            "proj:europe-west1:db-b",
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCloudSqlConnector(options =>
        {
            foreach (var instance in instances)
            {
                options.Instances.Add(instance);
            }
        });

        await using var provider = services.BuildServiceProvider();

        // Running the registered IHostedService binds a loopback listener per instance. The
        // listener binds synchronously and does not dial the instance, so no network/credentials
        // are required for this to succeed.
        var hostedServices = provider.GetServices<IHostedService>();
        foreach (var hosted in hostedServices)
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        var connector = provider.GetRequiredService<CloudSqlConnector>();
        foreach (var instance in instances)
        {
            // StartLocalProxyAsync is idempotent: it returns the proxy already started at startup.
            var endpoint = await connector.StartLocalProxyAsync(instance);
            Assert.Equal(System.Net.IPAddress.Loopback, endpoint.Address);
            Assert.True(endpoint.Port > 0);
        }
    }

    [Fact]
    public async Task NoConfiguredInstances_StartsCleanly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCloudSqlConnector();

        await using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>();
        foreach (var hosted in hostedServices)
        {
            await hosted.StartAsync(CancellationToken.None);
            await hosted.StopAsync(CancellationToken.None);
        }
    }
}
