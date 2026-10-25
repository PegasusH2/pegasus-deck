using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PegasusDeck.Service;

internal static class Certificates
{
    public static X509Certificate2 Load(PegasusOptions o)
    {
        if (o.SelfSignedCertificate) return CreateSelfSigned(o.AllowedHosts);
        using var pem = X509Certificate2.CreateFromPemFile(o.CertificatePath!, o.CertificateKeyPath);
        // SChannel (Windows) necesita la clave en un contenedor: se reimporta como PKCS#12.
        return new X509Certificate2(pem.Export(X509ContentType.Pkcs12));
    }

    /// <summary>Solo pruebas: certificado de 7 días para los hosts configurados.</summary>
    private static X509Certificate2 CreateSelfSigned(IEnumerable<string> hosts)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=Pegasus Deck (pruebas)", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        foreach (var h in hosts)
        {
            if (IPAddress.TryParse(h, out var ip)) san.AddIpAddress(ip);
            else san.AddDnsName(h);
        }
        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(7));
        return new X509Certificate2(cert.Export(X509ContentType.Pkcs12));
    }
}
