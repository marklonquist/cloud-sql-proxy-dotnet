using Npgsql;

namespace CloudSql.Connector.Npgsql;

/// <summary>
/// Npgsql integration for <see cref="CloudSqlConnector"/>.
/// </summary>
public static class NpgsqlCloudSqlExtensions
{
    /// <summary>
    /// Builds an <see cref="NpgsqlDataSource"/> that connects to a Cloud SQL for PostgreSQL
    /// instance through the in-process loopback proxy. The supplied
    /// <paramref name="baseConnectionString"/> provides <c>Database</c>, <c>Username</c> and any
    /// pool settings; <c>Host</c>, <c>Port</c> and SSL are overridden to target the proxy.
    /// </summary>
    /// <param name="connector">The Cloud SQL connector.</param>
    /// <param name="instanceConnectionName">The <c>project:region:instance</c> name.</param>
    /// <param name="baseConnectionString">A base connection string (database, username, pool options).</param>
    /// <param name="ipType">Which instance IP to dial; defaults to the connector's configured type.</param>
    /// <param name="useIamAuthentication">
    /// When <c>true</c>, the database password is supplied automatically as a refreshed Cloud SQL
    /// IAM access token. Set <c>Username</c> to the IAM principal's email in the base connection string.
    /// </param>
    /// <param name="configure">Optional hook to further customise the data source builder.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async Task<NpgsqlDataSource> CreateCloudSqlDataSourceAsync(
        this CloudSqlConnector connector,
        string instanceConnectionName,
        string baseConnectionString,
        IpType? ipType = null,
        bool useIamAuthentication = false,
        Action<NpgsqlDataSourceBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connector);

        var endpoint = await connector
            .StartLocalProxyAsync(instanceConnectionName, ipType, cancellationToken)
            .ConfigureAwait(false);

        var csb = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Host = endpoint.Address.ToString(),
            Port = endpoint.Port,
            // TLS is terminated by the connector against the instance; the loopback hop is plaintext.
            SslMode = SslMode.Disable,
        };

        var builder = new NpgsqlDataSourceBuilder(csb.ConnectionString);

        if (useIamAuthentication)
        {
            // IAM access tokens last ~1h; refresh ahead of expiry.
            builder.UsePeriodicPasswordProvider(
                (_, ct) => new ValueTask<string>(connector.GetIamDatabasePasswordAsync(ct)),
                TimeSpan.FromMinutes(50),
                TimeSpan.FromSeconds(5));
        }

        configure?.Invoke(builder);
        return builder.Build();
    }
}
