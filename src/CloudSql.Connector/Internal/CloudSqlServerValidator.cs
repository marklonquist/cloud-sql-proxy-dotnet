using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace CloudSql.Connector.Internal;

/// <summary>
/// Builds the <see cref="RemoteCertificateValidationCallback"/> used to authenticate a Cloud
/// SQL server during the mTLS handshake. The default platform validation cannot express
/// "trust exactly this one CA AND require this identity", so the connector validates the
/// chain against the instance's server CA only, then enforces the instance identity itself.
/// </summary>
internal static class CloudSqlServerValidator
{
    public static RemoteCertificateValidationCallback Create(ConnectionInfo info)
    {
        return (sender, certificate, chain, sslPolicyErrors) =>
        {
            if (certificate is null)
            {
                return false;
            }

            using var serverCert = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());

            if (!BuildsChainToInstanceCa(serverCert, info.ServerCaCertificate))
            {
                return false;
            }

            // CAS-issued instances carry a DNS SAN; legacy instances embed "project:instance"
            // in the certificate Common Name.
            return info.UseSanVerification && !string.IsNullOrEmpty(info.DnsName)
                ? MatchesDnsName(serverCert, info.DnsName!)
                : MatchesLegacyCommonName(serverCert, info.Instance.ServerCertCommonName);
        };
    }

    private static bool BuildsChainToInstanceCa(X509Certificate2 serverCert, X509Certificate2 serverCa)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(serverCa);
        // We perform our own identity check below; the chain only needs to root in the
        // instance CA.
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreEndRevocationUnknown
            | X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown;

        return chain.Build(serverCert);
    }

    private static bool MatchesLegacyCommonName(X509Certificate2 serverCert, string expectedCommonName)
    {
        var commonName = serverCert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        return string.Equals(commonName, expectedCommonName, StringComparison.Ordinal);
    }

    private static bool MatchesDnsName(X509Certificate2 serverCert, string expectedDnsName)
    {
        foreach (var dnsName in serverCert.GetNameInfoDnsNames())
        {
            if (string.Equals(dnsName, expectedDnsName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Fall back to the legacy CN check, matching the Google connectors' behaviour.
        var commonName = serverCert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        return string.Equals(commonName, expectedDnsName, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetNameInfoDnsNames(this X509Certificate2 cert)
    {
        // X509NameType.DnsName returns a single (first) entry on older runtimes; enumerate the
        // SAN extension directly for completeness.
        foreach (var extension in cert.Extensions)
        {
            if (extension is X509SubjectAlternativeNameExtension san)
            {
                foreach (var name in san.EnumerateDnsNames())
                {
                    yield return name;
                }
            }
        }
    }
}
