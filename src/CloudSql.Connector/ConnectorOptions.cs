using Google.Apis.Auth.OAuth2;

namespace CloudSql.Connector;

/// <summary>
/// Configuration for a <see cref="CloudSqlConnector"/>.
/// </summary>
public sealed class ConnectorOptions
{
    /// <summary>
    /// The default IP type used when a per-connection value is not supplied. Defaults to
    /// <see cref="IpType.Public"/>.
    /// </summary>
    public IpType DefaultIpType { get; set; } = IpType.Public;

    /// <summary>
    /// Instance connection names to start an in-process proxy for automatically at host startup,
    /// each on its own listener bound before the application begins serving requests. Resolve a
    /// started proxy's TCP endpoint with <see cref="CloudSqlConnector.StartLocalProxyAsync"/>
    /// (idempotent). Empty by default.
    /// <para>
    /// This is the in-process equivalent of the Cloud SQL Auth Proxy binary's <c>--instances</c>
    /// list and binds directly from configuration, for example an
    /// <c>"Instances": [ "project:region:instance" ]</c> array in <c>appsettings.json</c>.
    /// </para>
    /// <para>
    /// Each entry is a <c>project:region:instance</c> name with optional, binary-compatible query
    /// overrides: <c>?port=5000</c>, <c>?address=0.0.0.0</c>, <c>?unix-socket=/dir</c> or
    /// <c>?unix-socket-path=/dir/sock</c> (combine with <c>&amp;</c>). When a port is not given,
    /// it is assigned exactly as the binary does — see <see cref="Port"/>.
    /// </para>
    /// </summary>
    public IList<string> Instances { get; } = new List<string>();

    /// <summary>
    /// The starting TCP port for auto-started instance listeners (the Cloud SQL Auth Proxy
    /// binary's <c>--port</c>). When non-zero, configured instances without an explicit
    /// <c>?port=</c> are assigned this value, incrementing by one for each subsequent instance.
    /// When <c>0</c> (the default), each instance instead uses its database engine's default
    /// port (PostgreSQL 5432, MySQL 3306, SQL Server 1433), incrementing per engine on collision;
    /// the engine is detected from instance metadata at startup.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// The address auto-started instance listeners bind to (the Cloud SQL Auth Proxy binary's
    /// <c>--address</c>). Defaults to <c>127.0.0.1</c>. Override per instance with <c>?address=</c>.
    /// </summary>
    public string Address { get; set; } = "127.0.0.1";

    /// <summary>
    /// A directory in which to create a Unix domain socket per auto-started instance instead of a
    /// TCP listener (the Cloud SQL Auth Proxy binary's <c>--unix-socket</c>). When set, instances
    /// listen on <c>&lt;dir&gt;/&lt;instance&gt;</c> (PostgreSQL: <c>&lt;dir&gt;/&lt;instance&gt;/.s.PGSQL.5432</c>).
    /// Override per instance with <c>?unix-socket=</c> or <c>?unix-socket-path=</c>. <c>null</c> by default.
    /// </summary>
    public string? UnixSocketPath { get; set; }

    /// <summary>
    /// When <c>true</c>, the OAuth2 access token is embedded into the ephemeral client
    /// certificate and is expected to be used as the database password (Cloud SQL IAM
    /// database authentication). Defaults to <c>false</c>.
    /// </summary>
    public bool EnableIamAuthentication { get; set; }

    /// <summary>
    /// The credential used both to call the Cloud SQL Admin API and, when
    /// <see cref="EnableIamAuthentication"/> is enabled, to mint the database login token.
    /// When <c>null</c> the connector uses Application Default Credentials.
    /// </summary>
    public GoogleCredential? Credential { get; set; }

    /// <summary>
    /// Optional override of the Cloud SQL Admin API root URL (for example to target a
    /// regional or private endpoint). When <c>null</c> the default public endpoint is used.
    /// </summary>
    public string? AdminApiEndpoint { get; set; }

    /// <summary>
    /// A "quota project" charged for Admin API calls when ADC does not carry one.
    /// </summary>
    public string? QuotaProject { get; set; }

    /// <summary>
    /// How long before a cached ephemeral certificate expires that a background refresh is
    /// started. Defaults to 4 minutes, matching the Google connectors.
    /// </summary>
    public TimeSpan RefreshBuffer { get; set; } = TimeSpan.FromMinutes(4);

    /// <summary>
    /// The timeout applied to a single metadata + certificate refresh cycle. Defaults to
    /// 60 seconds.
    /// </summary>
    public TimeSpan RefreshTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
