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
