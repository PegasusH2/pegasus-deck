using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Nodes;
using PegasusDeck.Core.Network;
using PegasusDeck.Core.Storage;

namespace PegasusDeck.Service;

/// <summary>Reinicia el servicio instalado; null si arranca, si no el motivo.</summary>
internal interface IServiceRestarter
{
    string? Restart();
}

internal sealed class WindowsServiceRestarter(string name = "PegasusDeck") : IServiceRestarter
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(25);

    public string? Restart()
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending))
            {
                sc.Stop();
            }
            sc.WaitForStatus(ServiceControllerStatus.Stopped, Wait);
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, Wait);
            // Si no puede escuchar en la IP, el servicio arranca y se cierra enseguida: se comprueba un momento después.
            Thread.Sleep(TimeSpan.FromSeconds(3));
            sc.Refresh();
            return sc.Status == ServiceControllerStatus.Running ? null : "El servicio se ha detenido al arrancar.";
        }
        catch (Exception e) when (e is InvalidOperationException or System.ServiceProcess.TimeoutException or System.ComponentModel.Win32Exception)
        {
            return e.Message;
        }
    }
}

/// <summary>Resultado: código de salida de la orden local y texto para el usuario.</summary>
internal sealed record AddressChangeResult(int ExitCode, string Message)
{
    public const int Ok = 0, Rejected = 1, NoCa = 4, ServiceFailed = 5;
}

/// <summary>
/// Cambia la IP en la que escucha el servicio (ADR-034): la valida (privada y de este PC), actualiza
/// ListenAddresses/AllowedHosts en la configuración instalada y en su copia protegida, emite un certificado para
/// esa IP con la CA local de mkcert si el actual no la incluye, y reinicia el servicio. Si no arranca, lo deja
/// todo como estaba. Lo ejecuta «PegasusDeck.Service address set» con permiso de administrador.
/// </summary>
internal sealed class AddressChange(
    IReadOnlyList<string> settingsFiles, string? caRoot, IServiceRestarter restarter, Func<IReadOnlyList<LanAddress>> localAddresses)
{
    /// <summary>Validez del certificado nuevo: el máximo que acepta iOS para CA instaladas por el usuario (825 días).</summary>
    public static readonly TimeSpan CertificateLifetime = TimeSpan.FromDays(820);

    public AddressChangeResult Run(string ipText)
    {
        var (ip, problem) = LanAddresses.Check(ipText, localAddresses());
        if (ip is null) return new(AddressChangeResult.Rejected, problem!);

        var files = settingsFiles.Where(File.Exists).ToList();
        if (files.Count == 0) return new(AddressChangeResult.Rejected, "No se encuentra la configuración del servicio (appsettings.json).");
        var main = JsonNode.Parse(File.ReadAllText(files[0]))?["Pegasus"];
        var certPath = main?["CertificatePath"]?.GetValue<string>();
        var keyPath = main?["CertificateKeyPath"]?.GetValue<string>();
        if (certPath is null || keyPath is null) return new(AddressChangeResult.Rejected, "La configuración no indica el certificado HTTPS: reinstala con install.ps1.");

        // Copia de todo lo que se toca, para volver atrás.
        var backup = files.Append(certPath).Append(keyPath).Where(File.Exists).ToDictionary(f => f, File.ReadAllText);

        var newCert = false;
        if (!CertificateCovers(certPath, ip))
        {
            if (LoadCa(caRoot, out var why) is not { } ca) return new(AddressChangeResult.NoCa, $"No se puede usar la CA de mkcert para el certificado de la IP nueva: {why}");
            using (ca)
            {
                var (certPem, keyPem) = Issue(ca, ip, DateTimeOffset.UtcNow);
                AtomicFile.WriteAllText(keyPath, keyPem);
                AtomicFile.WriteAllText(certPath, certPem);
            }
            newCert = true;
        }

        foreach (var file in files) AtomicFile.WriteAllText(file, WithAddress(File.ReadAllText(file), ip));

        if (restarter.Restart() is { } failure)
        {
            foreach (var (file, text) in backup) AtomicFile.WriteAllText(file, text);
            var again = restarter.Restart();
            return new(AddressChangeResult.ServiceFailed,
                $"El servicio no arranca con {ip} ({failure}). Se ha dejado como estaba{(again is null ? "" : $", pero tampoco arranca ({again})")}.");
        }
        return new(AddressChangeResult.Ok, $"Escuchando en https://{ip}:{Port(files[0])}{(newCert ? " con un certificado nuevo" : "")}.");
    }

    /// <summary>Solo cambia ListenAddresses y AllowedHosts; el resto de la configuración queda igual.</summary>
    public static string WithAddress(string json, IPAddress ip)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("appsettings.json no es un objeto");
        var pegasus = root["Pegasus"] as JsonObject ?? new JsonObject();
        root["Pegasus"] = pegasus;
        pegasus["ListenAddresses"] = new JsonArray(ip.ToString());
        pegasus["AllowedHosts"] = new JsonArray(ip.ToString());
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static int Port(string file) => JsonNode.Parse(File.ReadAllText(file))?["Pegasus"]?["Port"]?.GetValue<int>() ?? 8443;

    public static bool CertificateCovers(string certPath, IPAddress ip)
    {
        try
        {
            using var cert = X509Certificate2.CreateFromPem(File.ReadAllText(certPath)); // solo el certificado (sin clave)
            // Al leer un PEM, la extensión SAN llega sin tipo: se interpreta a partir de sus bytes.
            var raw = cert.Extensions.Cast<X509Extension>().FirstOrDefault(e => e.Oid?.Value == "2.5.29.17");
            return raw is not null && new X509SubjectAlternativeNameExtension(raw.RawData).EnumerateIPAddresses().Contains(ip)
                && cert.NotAfter > DateTime.Now.AddDays(30);
        }
        catch (Exception e) when (e is CryptographicException or IOException)
        {
            return false;
        }
    }

    /// <summary>Carpeta de la CA de mkcert del usuario (la misma con la que se instaló el perfil en el iPad).</summary>
    public static string DefaultCaRoot() => Core.Security.MkcertRoot.Default();

    public static X509Certificate2? LoadCa(string? caRoot) => LoadCa(caRoot, out _);

    /// <summary>CA con su clave privada, o null con el motivo (falta, no se puede leer o no es una CA).</summary>
    public static X509Certificate2? LoadCa(string? caRoot, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(caRoot))
        {
            reason = "no se sabe en qué carpeta está.";
            return null;
        }
        var cert = Path.Combine(caRoot, "rootCA.pem");
        var key = Path.Combine(caRoot, "rootCA-key.pem");
        var missing = new[] { cert, key }.Where(f => !File.Exists(f)).ToList();
        if (missing.Count > 0)
        {
            reason = $"no existe {string.Join(" ni ", missing)}. Hace falta la misma CA que instalaste en el iPad.";
            return null;
        }
        try
        {
            var ca = X509Certificate2.CreateFromPemFile(cert, key);
            var basic = ca.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
            if (basic is { CertificateAuthority: true } && ca.HasPrivateKey) return ca;
            ca.Dispose();
            reason = $"{cert} no es una CA o no tiene su clave.";
            return null;
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            reason = $"no se puede leer ({e.GetType().Name}: {e.Message})";
            return null;
        }
    }

    /// <summary>Certificado de servidor para la IP, firmado por la CA (como mkcert): RSA 2048, SHA-256, serverAuth.</summary>
    public static (string CertPem, string KeyPem) Issue(X509Certificate2 ca, IPAddress ip, DateTimeOffset now)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"O=Pegasus Deck, CN={ip}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(ip);
        req.CertificateExtensions.Add(san.Build(critical: false));
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        if (ca.Extensions.Cast<X509Extension>().Any(e => e.Oid?.Value == "2.5.29.14"))
        {
            req.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca, includeKeyIdentifier: true, includeIssuerAndSerial: false));
        }
        var notAfter = now + CertificateLifetime;
        if (notAfter > ca.NotAfter) notAfter = ca.NotAfter;
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7f;
        using var cert = req.Create(ca, now.AddMinutes(-5), notAfter, serial);
        return (cert.ExportCertificatePem() + "\n", rsa.ExportPkcs8PrivateKeyPem() + "\n");
    }
}
