using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using CloudSql.Connector.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CloudSql.Connector;

/// <summary>
/// Connects to Google Cloud SQL instances from inside your process — no Cloud SQL Auth Proxy
/// binary or sidecar required. The connector discovers instance metadata, rotates a short-lived
/// client certificate ahead of expiry, and performs the mTLS handshake itself.
/// <para>
/// Use <see cref="StartLocalProxyAsync"/> to obtain a loopback endpoint for any ADO.NET driver,
/// or <see cref="ConnectAsync"/> to obtain a raw, already-authenticated <see cref="Stream"/>.
/// </para>
/// </summary>
public sealed class CloudSqlConnector : IAsyncDisposable
{
    /// <summary>The TLS port every Cloud SQL instance exposes for the connector protocol.</summary>
    private const int ServerProxyPort = 3307;

    private readonly ConnectorOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Lazy<Task<CloudSqlAdminClient>> _adminClient;
    private readonly RSA _rsa;
    private readonly Refresher _refresher;

    private readonly ConcurrentDictionary<string, Lazy<RefreshAheadCache>> _caches = new();
    private readonly ConcurrentDictionary<string, Lazy<LocalProxyServer>> _proxies = new();
    private int _disposed;

    private CloudSqlConnector(ConnectorOptions options, ILoggerFactory loggerFactory)
    {
        _options = options;
        _loggerFactory = loggerFactory;
        _rsa = RSA.Create(2048);
        // The admin client resolves Application Default Credentials, which can touch the
        // network/metadata server — defer it to first use so the connector itself constructs
        // synchronously (and is a clean DI singleton).
        _adminClient = new Lazy<Task<CloudSqlAdminClient>>(
            () => CloudSqlAdminClient.CreateAsync(options, CancellationToken.None));
        _refresher = new Refresher(_ => _adminClient.Value, options, _rsa);
    }

    /// <summary>
    /// Creates a connector. Credentials (Application Default Credentials unless
    /// <see cref="ConnectorOptions.Credential"/> is set) are resolved lazily on first use.
    /// </summary>
    public static CloudSqlConnector Create(
        ConnectorOptions? options = null,
        ILoggerFactory? loggerFactory = null)
        => new(options ?? new ConnectorOptions(), loggerFactory ?? NullLoggerFactory.Instance);

    /// <summary>
    /// Creates a connector and eagerly resolves credentials so configuration errors surface
    /// immediately rather than on the first connection.
    /// </summary>
    public static async Task<CloudSqlConnector> CreateAsync(
        ConnectorOptions? options = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        var connector = Create(options, loggerFactory);
        cancellationToken.ThrowIfCancellationRequested();
        _ = await connector._adminClient.Value.ConfigureAwait(false);
        return connector;
    }

    /// <summary>
    /// Starts (or returns the already-running) in-process loopback proxy for an instance and
    /// returns the <c>127.0.0.1</c> endpoint a database driver should connect to. The proxy is
    /// reused across calls and lives until the connector is disposed.
    /// </summary>
    /// <param name="instanceConnectionName">The <c>project:region:instance</c> name.</param>
    /// <param name="ipType">Which instance IP to dial; defaults to <see cref="ConnectorOptions.DefaultIpType"/>.</param>
    /// <param name="cancellationToken">Unused; present for API symmetry. The proxy outlives any single call.</param>
    public Task<IPEndPoint> StartLocalProxyAsync(
        string instanceConnectionName,
        IpType? ipType = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var instance = InstanceConnectionName.Parse(instanceConnectionName);
        var effectiveIpType = ipType ?? _options.DefaultIpType;
        var key = $"{instance.Original}|{effectiveIpType}";

        var proxy = _proxies.GetOrAdd(key, _ => new Lazy<LocalProxyServer>(() =>
            LocalProxyServer.Start(
                ct => ConnectAsync(instanceConnectionName, effectiveIpType, ct),
                _loggerFactory.CreateLogger<LocalProxyServer>(),
                instance.Original))).Value;

        return Task.FromResult(proxy.Endpoint);
    }

    /// <summary>
    /// Opens a raw, mTLS-authenticated <see cref="Stream"/> to the instance's database server.
    /// The caller owns and must dispose the returned stream. Most callers should prefer
    /// <see cref="StartLocalProxyAsync"/>, which makes this usable from any ADO.NET driver.
    /// </summary>
    public async Task<Stream> ConnectAsync(
        string instanceConnectionName,
        IpType? ipType = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var instance = InstanceConnectionName.Parse(instanceConnectionName);
        var effectiveIpType = ipType ?? _options.DefaultIpType;
        var cache = GetCache(instance);
        var info = await cache.GetConnectionInfoAsync(cancellationToken).ConfigureAwait(false);
        var ip = info.ResolveIpAddress(effectiveIpType);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        SslStream? ssl = null;
        try
        {
            await socket.ConnectAsync(IPAddress.Parse(ip), ServerProxyPort, cancellationToken)
                .ConfigureAwait(false);

            ssl = new SslStream(new NetworkStream(socket, ownsSocket: true), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(info.CreateSslOptions(), cancellationToken)
                .ConfigureAwait(false);
            return ssl;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A handshake failure usually means stale metadata/certificate — evict so the next
            // attempt fetches fresh state.
            cache.ForceRefresh();
            if (ssl is not null)
            {
                await ssl.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                socket.Dispose();
            }

            throw;
        }
    }

    /// <summary>
    /// Returns the OAuth2 access token to use as the database password when Cloud SQL IAM
    /// database authentication is enabled. The username is the IAM principal's email.
    /// </summary>
    public async Task<string> GetIamDatabasePasswordAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var adminClient = await _adminClient.Value.ConfigureAwait(false);
        return await adminClient.GetIamLoginTokenAsync(cancellationToken).ConfigureAwait(false);
    }

    private RefreshAheadCache GetCache(InstanceConnectionName instance)
        => _caches.GetOrAdd(instance.Original, _ => new Lazy<RefreshAheadCache>(() =>
            new RefreshAheadCache(
                instance,
                _refresher,
                _options,
                _loggerFactory.CreateLogger<RefreshAheadCache>()))).Value;

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var proxy in _proxies.Values)
        {
            if (proxy.IsValueCreated)
            {
                await proxy.Value.DisposeAsync().ConfigureAwait(false);
            }
        }

        foreach (var cache in _caches.Values)
        {
            if (cache.IsValueCreated)
            {
                await cache.Value.DisposeAsync().ConfigureAwait(false);
            }
        }

        if (_adminClient.IsValueCreated)
        {
            try
            {
                (await _adminClient.Value.ConfigureAwait(false)).Dispose();
            }
            catch
            {
                // Credential resolution failed; nothing to dispose.
            }
        }

        _rsa.Dispose();
    }
}
