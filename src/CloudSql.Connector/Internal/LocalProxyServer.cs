using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace CloudSql.Connector.Internal;

/// <summary>
/// An in-process listener that lets ADO.NET drivers — none of which expose a custom
/// socket/transport hook — reach a Cloud SQL instance. It binds a local endpoint (a loopback TCP
/// port by default, or a Unix domain socket), and for each accepted local connection it dials the
/// instance over mTLS and pumps bytes in both directions. This is the Cloud SQL Auth Proxy's job,
/// run inside your process instead of as a sidecar. Local traffic never leaves the host, so the
/// driver connects without TLS.
/// </summary>
internal sealed class LocalProxyServer : IAsyncDisposable
{
    private readonly Socket _listener;
    private readonly Func<CancellationToken, Task<Stream>> _dial;
    private readonly ILogger _logger;
    private readonly string _instanceLabel;
    private readonly string? _unixSocketPath;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    private LocalProxyServer(
        Socket listener,
        Func<CancellationToken, Task<Stream>> dial,
        ILogger logger,
        string instanceLabel,
        string? unixSocketPath)
    {
        _listener = listener;
        _dial = dial;
        _logger = logger;
        _instanceLabel = instanceLabel;
        _unixSocketPath = unixSocketPath;
    }

    /// <summary>The local endpoint a database driver should connect to.</summary>
    public EndPoint LocalEndPoint { get; private set; } = null!;

    /// <summary>
    /// Starts a listener bound to <paramref name="bindEndpoint"/>. Pass an
    /// <see cref="IPEndPoint"/> with port <c>0</c> for an OS-assigned loopback port, or a
    /// <see cref="UnixDomainSocketEndPoint"/> for a Unix domain socket.
    /// </summary>
    public static LocalProxyServer Start(
        Func<CancellationToken, Task<Stream>> dial,
        ILogger logger,
        string instanceLabel,
        EndPoint bindEndpoint)
    {
        string? unixSocketPath = null;
        Socket listener;

        if (bindEndpoint is UnixDomainSocketEndPoint)
        {
            unixSocketPath = bindEndpoint.ToString();
            // A leftover socket file from a previous run would make Bind fail with "address in use".
            if (unixSocketPath is not null && File.Exists(unixSocketPath))
            {
                File.Delete(unixSocketPath);
            }

            var directory = Path.GetDirectoryName(unixSocketPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        }
        else
        {
            listener = new Socket(bindEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        }

        listener.Bind(bindEndpoint);
        listener.Listen();

        var server = new LocalProxyServer(listener, dial, logger, instanceLabel, unixSocketPath)
        {
            LocalEndPoint = listener.LocalEndPoint!,
        };
        server._acceptLoop = Task.Run(() => server.AcceptLoopAsync(server._cts.Token));
        return server;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Local accept loop for {Instance} stopped.", _instanceLabel);
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(Socket client, CancellationToken cancellationToken)
    {
        // NoDelay is a TCP option; setting it on a Unix domain socket throws.
        if (client.ProtocolType == ProtocolType.Tcp)
        {
            client.NoDelay = true;
        }

        await using var local = new NetworkStream(client, ownsSocket: true);
        Stream? upstream = null;
        try
        {
            upstream = await _dial(cancellationToken).ConfigureAwait(false);

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

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Dispose();

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

        if (_unixSocketPath is not null && File.Exists(_unixSocketPath))
        {
            try
            {
                File.Delete(_unixSocketPath);
            }
            catch
            {
                // Best-effort cleanup of the socket file.
            }
        }

        _cts.Dispose();
    }
}
