using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Google.Apis.SQLAdmin.v1.Data;

namespace CloudSql.Connector.Internal;

/// <summary>
/// Performs a single metadata + ephemeral-certificate refresh for an instance and assembles a
/// <see cref="ConnectionInfo"/>. The RSA key pair is generated once per connector and reused
/// across refreshes; only the signed certificate rotates.
/// </summary>
internal sealed class Refresher
{
    private const string GoogleManagedInternalCa = "GOOGLE_MANAGED_INTERNAL_CA";

    private readonly Func<CancellationToken, Task<CloudSqlAdminClient>> _adminClientFactory;
    private readonly ConnectorOptions _options;
    private readonly RSA _rsa;
    private readonly string _publicKeyPem;

    public Refresher(
        Func<CancellationToken, Task<CloudSqlAdminClient>> adminClientFactory,
        ConnectorOptions options,
        RSA rsa)
    {
        _adminClientFactory = adminClientFactory;
        _options = options;
        _rsa = rsa;
        _publicKeyPem = PemEncoding.WriteString("PUBLIC KEY", rsa.ExportSubjectPublicKeyInfo());
    }

    public async Task<ConnectionInfo> RefreshAsync(
        InstanceConnectionName instance,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.RefreshTimeout);
        var ct = timeoutCts.Token;

        var adminClient = await _adminClientFactory(ct).ConfigureAwait(false);

        string? iamLoginToken = _options.EnableIamAuthentication
            ? await adminClient.GetIamLoginTokenAsync(ct).ConfigureAwait(false)
            : null;

        // Metadata and certificate signing are independent — fetch concurrently.
        var settingsTask = adminClient.GetConnectSettingsAsync(instance, ct);
        var certTask = adminClient.GenerateEphemeralCertAsync(instance, _publicKeyPem, iamLoginToken, ct);
        await Task.WhenAll(settingsTask, certTask).ConfigureAwait(false);

        var settings = settingsTask.Result;
        var ephemeralCert = certTask.Result;

        var ipAddresses = MapIpAddresses(settings);
        var serverCa = LoadServerCa(settings, instance);
        var clientCert = BuildClientCertificate(ephemeralCert, instance);

        var useSan = !string.IsNullOrEmpty(settings.DnsName)
            && !string.Equals(settings.ServerCaMode, GoogleManagedInternalCa, StringComparison.Ordinal);

        return new ConnectionInfo(
            instance,
            ipAddresses,
            clientCert,
            serverCa,
            settings.DnsName,
            useSan,
            new DateTimeOffset(clientCert.NotAfter.ToUniversalTime(), TimeSpan.Zero));
    }

    private static IReadOnlyDictionary<IpType, string> MapIpAddresses(ConnectSettings settings)
    {
        var map = new Dictionary<IpType, string>();
        if (settings.IpAddresses is not null)
        {
            foreach (var ip in settings.IpAddresses)
            {
                if (string.IsNullOrEmpty(ip.IpAddress))
                {
                    continue;
                }

                switch (ip.Type)
                {
                    case "PRIMARY":
                        map[IpType.Public] = ip.IpAddress;
                        break;
                    case "PRIVATE":
                        map[IpType.Private] = ip.IpAddress;
                        break;
                }
            }
        }

        // A Private Service Connect instance exposes no IP in connectSettings; the PSC DNS
        // name is dialed instead.
        if (settings.PscEnabled == true && !string.IsNullOrEmpty(settings.DnsName))
        {
            map[IpType.Psc] = settings.DnsName;
        }

        if (map.Count == 0)
        {
            throw new InvalidOperationException(
                "Cloud SQL connectSettings returned no usable IP addresses.");
        }

        return map;
    }

    private static X509Certificate2 LoadServerCa(ConnectSettings settings, InstanceConnectionName instance)
    {
        var pem = settings.ServerCaCert?.Cert
            ?? throw new InvalidOperationException(
                $"connectSettings for '{instance}' did not include a server CA certificate.");

        return X509Certificate2.CreateFromPem(pem);
    }

    private X509Certificate2 BuildClientCertificate(SslCert ephemeralCert, InstanceConnectionName instance)
    {
        var pem = ephemeralCert?.Cert
            ?? throw new InvalidOperationException(
                $"generateEphemeralCert for '{instance}' returned no certificate.");

        using var certOnly = X509Certificate2.CreateFromPem(pem);
        using var withKey = certOnly.CopyWithPrivateKey(_rsa);

        // Round-trip through PKCS#12 so the key is usable by SslStream on every platform
        // (notably Windows, where ephemeral keys from CopyWithPrivateKey are not accepted).
        return X509CertificateLoader.LoadPkcs12(
            withKey.Export(X509ContentType.Pkcs12),
            password: null,
            keyStorageFlags: X509KeyStorageFlags.Exportable);
    }
}
