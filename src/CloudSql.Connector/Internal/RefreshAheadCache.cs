using Microsoft.Extensions.Logging;

namespace CloudSql.Connector.Internal;

/// <summary>
/// Keeps a single instance's <see cref="ConnectionInfo"/> fresh. The first call triggers an
/// initial refresh; thereafter a background refresh is scheduled to complete shortly before
/// the current certificate expires, so callers almost always observe a ready, valid result.
/// A failed mTLS handshake can <see cref="ForceRefresh"/> to evict early.
/// </summary>
internal sealed class RefreshAheadCache : IAsyncDisposable
{
    private readonly InstanceConnectionName _instance;
    private readonly Refresher _refresher;
    private readonly ConnectorOptions _options;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();

    private Task<ConnectionInfo> _current;
    private bool _disposed;

    public RefreshAheadCache(
        InstanceConnectionName instance,
        Refresher refresher,
        ConnectorOptions options,
        ILogger logger)
    {
        _instance = instance;
        _refresher = refresher;
        _options = options;
        _logger = logger;
        _current = StartRefresh();
    }

    public async Task<ConnectionInfo> GetConnectionInfoAsync(CancellationToken cancellationToken)
    {
        Task<ConnectionInfo> task;
        lock (_gate)
        {
            task = _current;
        }

        var info = await task.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (info.Expiration <= DateTimeOffset.UtcNow)
        {
            ForceRefresh();
            lock (_gate)
            {
                task = _current;
            }

            info = await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return info;
    }

    /// <summary>
    /// Evicts the current result and starts an immediate refresh. Safe to call after a failed
    /// handshake; a refresh already in flight is left to complete.
    /// </summary>
    public void ForceRefresh()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_current.IsCompleted)
            {
                _current = StartRefresh();
            }
        }
    }

    private Task<ConnectionInfo> StartRefresh() => Task.Run(() => RefreshWithRetryAsync(_cts.Token));

    private async Task<ConnectionInfo> RefreshWithRetryAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var info = await _refresher.RefreshAsync(_instance, cancellationToken).ConfigureAwait(false);
                ScheduleNextRefresh(info.Expiration);
                _logger.LogDebug(
                    "Refreshed Cloud SQL connection info for {Instance}; certificate valid until {Expiration:o}.",
                    _instance, info.Expiration);
                return info;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                _logger.LogWarning(
                    ex,
                    "Refresh attempt {Attempt}/{Max} for {Instance} failed; retrying in {Backoff}.",
                    attempt, maxAttempts, _instance, backoff);
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void ScheduleNextRefresh(DateTimeOffset expiration)
    {
        var delay = expiration - _options.RefreshBuffer - DateTimeOffset.UtcNow;
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (_gate)
            {
                if (!_disposed)
                {
                    _current = StartRefresh();
                }
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        Task<ConnectionInfo> current;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            current = _current;
        }

        await _cts.CancelAsync().ConfigureAwait(false);

        try
        {
            var info = await current.ConfigureAwait(false);
            info.ClientCertificate.Dispose();
            info.ServerCaCertificate.Dispose();
        }
        catch
        {
            // A failed/cancelled refresh has nothing to dispose.
        }

        _cts.Dispose();
    }
}
