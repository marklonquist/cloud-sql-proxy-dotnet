using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.SQLAdmin.v1;
using Google.Apis.SQLAdmin.v1.Data;

namespace CloudSql.Connector.Internal;

/// <summary>
/// Thin wrapper over the generated Cloud SQL Admin API client that exposes only the two
/// calls the connector needs: <c>connectSettings</c> (instance metadata) and
/// <c>generateEphemeralCert</c> (sign a short-lived client certificate).
/// </summary>
internal sealed class CloudSqlAdminClient : IDisposable
{
    private const string LoginScope = "https://www.googleapis.com/auth/sqlservice.login";

    private readonly SQLAdminService _service;
    private readonly GoogleCredential _credential;

    private CloudSqlAdminClient(SQLAdminService service, GoogleCredential credential)
    {
        _service = service;
        _credential = credential;
    }

    public static async Task<CloudSqlAdminClient> CreateAsync(
        ConnectorOptions options,
        CancellationToken cancellationToken)
    {
        var credential = options.Credential
            ?? await GoogleCredential.GetApplicationDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (credential.IsCreateScopedRequired)
        {
            credential = credential.CreateScoped(SQLAdminService.Scope.SqlserviceAdmin);
        }

        if (!string.IsNullOrEmpty(options.QuotaProject))
        {
            credential = credential.CreateWithQuotaProject(options.QuotaProject);
        }

        var initializer = new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "cloud-sql-proxy-dotnet",
        };

        if (!string.IsNullOrEmpty(options.AdminApiEndpoint))
        {
            initializer.BaseUri = options.AdminApiEndpoint;
        }

        return new CloudSqlAdminClient(new SQLAdminService(initializer), credential);
    }

    /// <summary>
    /// Fetches <c>connectSettings</c> for the instance: IP addresses, the server CA
    /// certificate, the CA mode, and DNS metadata.
    /// </summary>
    public Task<ConnectSettings> GetConnectSettingsAsync(
        InstanceConnectionName instance,
        CancellationToken cancellationToken)
        => _service.Connect.Get(instance.Project, instance.Instance).ExecuteAsync(cancellationToken);

    /// <summary>
    /// Sends the locally generated public key to be signed into a short-lived client
    /// certificate. When <paramref name="iamLoginToken"/> is supplied it is embedded into
    /// the certificate (Cloud SQL IAM database authentication).
    /// </summary>
    public async Task<SslCert> GenerateEphemeralCertAsync(
        InstanceConnectionName instance,
        string publicKeyPem,
        string? iamLoginToken,
        CancellationToken cancellationToken)
    {
        var body = new GenerateEphemeralCertRequest
        {
            PublicKey = publicKeyPem,
            AccessToken = iamLoginToken,
        };

        var response = await _service.Connect
            .GenerateEphemeralCert(body, instance.Project, instance.Instance)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return response.EphemeralCert;
    }

    /// <summary>
    /// Mints an OAuth2 access token scoped for Cloud SQL IAM login. The same token is both
    /// embedded into the ephemeral certificate and used as the database password.
    /// </summary>
    public Task<string> GetIamLoginTokenAsync(CancellationToken cancellationToken)
    {
        ITokenAccess tokenSource = _credential.CreateScoped(LoginScope);
        return tokenSource.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
    }

    public void Dispose() => _service.Dispose();
}
