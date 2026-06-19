using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace CloudSql.Connector.Internal;

/// <summary>
/// An in-process loopback listener that lets ADO.NET drivers — none of which expose a custom
/// socket/transport hook — reach a Cloud SQL instance. It binds <c>127.0.0.1:0</c>, and for
/// each accepted local connection it dials the instance over mTLS and pumps bytes in both
/// directions. This is the Cloud SQL Auth Proxy's job, run inside your process instead of as a
/// sidecar. Loopback traffic never leaves the host, so the driver connects without TLS.
/// </summary>
internal sealed class LocalProxyServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<CancellationToken, Task<Stream>> _dial;
    private readonly ILogger _logger;
    private readonly string _instanceLabel;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    private LocalProxyServer(
        TcpListener listener,
        Func<CancellationToken, Task<Stream>> dial,
        ILogger logger,
        string instanceLabel)
    {
        _listener = listener;
        _dial = dial;
        _logger = logger;
        _instanceLabel = instanceLabel;
    }

    /// <summary>The loopback endpoint a database driver should connect to.</summary>
    public IPEndPoint Endpoint { get; private set; } = null!;

    public static LocalProxyServer Start(
        Func<CancellationToken, Task<Stream>> dial,
        ILogger logger,
        string instanceLabel)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = new LocalProxyServer(listener, dial, logger, instanceLabel)
        {
            Endpoint = (IPEndPoint)listener.LocalEndpoint,
        };
        server._acceptLoop = Task.Run(() => server.AcceptLoopAsync(server._cts.Token));
        return server;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Loopback accept loop for {Instance} stopped.", _instanceLabel);
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            client.NoDelay = true;
            Stream? upstream = null;
            try
            {
                upstream = await _dial(cancellationToken).ConfigureAwait(false);
                var local = client.GetStream();

                var pumpUp = local.CopyToAsync(upstream, cancellationToken);
                var pumpDown = upstream.CopyToAsync(local, cancellationToken);
                await Task.WhenAny(pumpUp, pumpDown).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Proxied connection to {Instance} failed.", _instanceLabel);
            }
            finally
            {
                if (upstream is not null)
                {
                    await upstream.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Stop();

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch
            {
                // Accept loop teardown errors are not actionable.
            }
        }

        _cts.Dispose();
    }
}
