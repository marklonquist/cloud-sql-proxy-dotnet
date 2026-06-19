using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace CloudSql.Connector.Internal;

/// <summary>
/// An immutable snapshot of everything needed to open one mTLS connection to a Cloud SQL
/// instance: its reachable IP addresses, the freshly signed client certificate, and the
/// server-identity validator. Produced by <see cref="Refresher"/> and cached, then replaced
/// shortly before the client certificate expires.
/// </summary>
internal sealed class ConnectionInfo
{
    public ConnectionInfo(
        InstanceConnectionName instance,
        IReadOnlyDictionary<IpType, string> ipAddresses,
        X509Certificate2 clientCertificate,
        X509Certificate2 serverCaCertificate,
        string? dnsName,
        bool useSanVerification,
        DateTimeOffset expiration)
    {
        Instance = instance;
        IpAddresses = ipAddresses;
        ClientCertificate = clientCertificate;
        ServerCaCertificate = serverCaCertificate;
        DnsName = dnsName;
        UseSanVerification = useSanVerification;
        Expiration = expiration;
    }

    public InstanceConnectionName Instance { get; }

    public IReadOnlyDictionary<IpType, string> IpAddresses { get; }

    public X509Certificate2 ClientCertificate { get; }

    public X509Certificate2 ServerCaCertificate { get; }

    public string? DnsName { get; }

    public bool UseSanVerification { get; }

    public DateTimeOffset Expiration { get; }

    /// <summary>
    /// Resolves the IP address for the requested <paramref name="ipType"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The instance has no such IP configured.</exception>
    public string ResolveIpAddress(IpType ipType)
    {
        if (IpAddresses.TryGetValue(ipType, out var ip))
        {
            return ip;
        }

        throw new InvalidOperationException(
            $"Cloud SQL instance '{Instance}' has no {ipType} IP address. " +
            $"Available: {string.Join(", ", IpAddresses.Keys)}. " +
            "Check the instance's IP configuration or request a different IpType.");
    }

    /// <summary>
    /// Builds the client-side TLS options for a handshake against this instance: presents the
    /// ephemeral client certificate and validates the server against the instance CA and
    /// identity (SAN/DNS for CAS instances, legacy <c>project:instance</c> CN otherwise).
    /// </summary>
    public SslClientAuthenticationOptions CreateSslOptions()
    {
        var targetHost = UseSanVerification && !string.IsNullOrEmpty(DnsName)
            ? DnsName!
            : Instance.ServerCertCommonName;

        return new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            ClientCertificates = new X509CertificateCollection { ClientCertificate },
            RemoteCertificateValidationCallback = CloudSqlServerValidator.Create(this),
            // Cloud SQL's proxy port (3307) terminates TLS directly on connect.
            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12
                | System.Security.Authentication.SslProtocols.Tls13,
        };
    }
}
