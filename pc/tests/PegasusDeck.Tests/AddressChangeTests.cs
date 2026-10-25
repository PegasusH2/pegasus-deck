using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using PegasusDeck.Core.Network;
using PegasusDeck.Service;

namespace PegasusDeck.Tests;

/// <summary>ADR-034: cambiar la IP de la LAN desde la bandeja del PC.</summary>
public class CambioDeIpTests : IDisposable
{
    private static readonly IPAddress Old = IPAddress.Parse("192.168.1.23");
    private static readonly IPAddress New = IPAddress.Parse("192.168.1.24");
    private static readonly LanAddress[] Local = [new(New, "Ethernet", true), new(IPAddress.Parse("192.168.56.1"), "VirtualBox", false)];

    private readonly TempDir _dir = new();
    private readonly string _caRoot;
    private readonly string _install;
    private readonly string _data;
    private readonly string _cert;
    private readonly string _key;

    public CambioDeIpTests()
    {
        _caRoot = Path.Combine(_dir.Path, "mkcert");
        Directory.CreateDirectory(_caRoot);
        using (var rsa = RSA.Create(2048))
        {
            var req = new CertificateRequest("CN=mkcert pruebas", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
            using var ca = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
            File.WriteAllText(Path.Combine(_caRoot, "rootCA.pem"), ca.ExportCertificatePem());
            File.WriteAllText(Path.Combine(_caRoot, "rootCA-key.pem"), rsa.ExportPkcs8PrivateKeyPem());
        }
        _cert = Path.Combine(_dir.Path, "pegasus-cert.pem");
        _key = Path.Combine(_dir.Path, "pegasus-key.pem");
        using (var ca = AddressChange.LoadCa(_caRoot)!)
        {
            var (c, k) = AddressChange.Issue(ca, Old, DateTimeOffset.UtcNow);
            File.WriteAllText(_cert, c);
            File.WriteAllText(_key, k);
        }
        var json = $$"""
            { "Logging": { "LogLevel": { "Default": "Information" } },
              "Pegasus": { "Port": 8443, "ListenAddresses": ["192.168.1.23"], "AllowedHosts": ["192.168.1.23"],
                           "CertificatePath": {{System.Text.Json.JsonSerializer.Serialize(_cert)}}, "CertificateKeyPath": {{System.Text.Json.JsonSerializer.Serialize(_key)}},
                           "WebRoot": "C:\\Program Files\\PegasusDeck\\wwwroot" } }
            """;
        _install = Path.Combine(_dir.Path, "install-appsettings.json");
        _data = Path.Combine(_dir.Path, "data-appsettings.json");
        File.WriteAllText(_install, json);
        File.WriteAllText(_data, json);
    }

    public void Dispose()
    {
        _dir.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class FakeRestarter(params string?[] results) : IServiceRestarter
    {
        private int _n;
        public int Calls => _n;
        public string? Restart() => results[Math.Min(_n++, results.Length - 1)];
    }

    private AddressChange Change(IServiceRestarter restarter, string? caRoot = null) =>
        new([_install, _data], caRoot ?? _caRoot, restarter, () => Local);

    [Theory]
    [InlineData("1", "IPv4")]
    [InlineData("192.168.1", "IPv4")]
    [InlineData("8.8.8.8", "red local")]
    [InlineData("0.0.0.0", "red local")]
    [InlineData("192.168.1.99", "no tiene ahora")]
    [InlineData("192.168.1.24; rm", "IPv4")]
    public void Solo_se_acepta_una_IP_privada_que_tenga_este_PC(string text, string motivo)
    {
        var (ip, problem) = LanAddresses.Check(text, Local);
        Assert.Null(ip);
        Assert.Contains(motivo, problem, StringComparison.Ordinal);
        Assert.Equal(New, LanAddresses.Check(" 192.168.1.24 ", Local).Ip);
    }

    [Fact]
    public void Cambia_solo_la_IP_crea_el_certificado_con_la_CA_y_reinicia()
    {
        var before = JsonNode.Parse(File.ReadAllText(_install))!;
        var restarter = new FakeRestarter((string?)null);
        var r = Change(restarter).Run("192.168.1.24");

        Assert.Equal(AddressChangeResult.Ok, r.ExitCode);
        Assert.Contains("https://192.168.1.24:8443", r.Message, StringComparison.Ordinal);
        Assert.Equal(1, restarter.Calls);
        foreach (var file in new[] { _install, _data })
        {
            var after = JsonNode.Parse(File.ReadAllText(file))!;
            Assert.Equal("192.168.1.24", after["Pegasus"]!["ListenAddresses"]![0]!.GetValue<string>());
            Assert.Equal("192.168.1.24", after["Pegasus"]!["AllowedHosts"]![0]!.GetValue<string>());
            Assert.Single(after["Pegasus"]!["ListenAddresses"]!.AsArray());
            foreach (var key in new[] { "Port", "CertificatePath", "CertificateKeyPath", "WebRoot" })
            {
                Assert.Equal(before["Pegasus"]![key]!.ToJsonString(), after["Pegasus"]![key]!.ToJsonString());
            }
            Assert.Equal(before["Logging"]!.ToJsonString(), after["Logging"]!.ToJsonString());
        }

        // Certificado nuevo para la IP, firmado por la misma CA que tiene el iPad, y su clave.
        using var cert = X509Certificate2.CreateFromPemFile(_cert, _key);
        Assert.True(AddressChange.CertificateCovers(_cert, New));
        Assert.False(AddressChange.CertificateCovers(_cert, Old));
        Assert.True(cert.NotAfter - cert.NotBefore <= TimeSpan.FromDays(825));
        using var ca = X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(_caRoot, "rootCA.pem")));
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        Assert.True(chain.Build(cert));
        Assert.Contains(cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single().EnhancedKeyUsages.Cast<Oid>(), o => o.Value == "1.3.6.1.5.5.7.3.1");
    }

    [Fact]
    public void Si_el_certificado_ya_incluye_la_IP_no_hace_falta_la_CA()
    {
        File.WriteAllText(_install, AddressChange.WithAddress(File.ReadAllText(_install), New));
        using (var ca = AddressChange.LoadCa(_caRoot)!)
        {
            var (c, k) = AddressChange.Issue(ca, New, DateTimeOffset.UtcNow);
            File.WriteAllText(_cert, c);
            File.WriteAllText(_key, k);
        }
        var certBefore = File.ReadAllText(_cert);
        var r = Change(new FakeRestarter((string?)null), caRoot: Path.Combine(_dir.Path, "no-existe")).Run("192.168.1.24");
        Assert.Equal(AddressChangeResult.Ok, r.ExitCode);
        Assert.Equal(certBefore, File.ReadAllText(_cert));
    }

    [Fact]
    public void Sin_la_CA_no_cambia_nada()
    {
        var install = File.ReadAllText(_install);
        var cert = File.ReadAllText(_cert);
        var restarter = new FakeRestarter((string?)null);
        var r = Change(restarter, caRoot: Path.Combine(_dir.Path, "no-existe")).Run("192.168.1.24");
        Assert.Equal(AddressChangeResult.NoCa, r.ExitCode);
        Assert.Equal(install, File.ReadAllText(_install));
        Assert.Equal(cert, File.ReadAllText(_cert));
        Assert.Equal(0, restarter.Calls);
    }

    [Fact]
    public void IP_rechazada_no_toca_nada()
    {
        var install = File.ReadAllText(_install);
        var restarter = new FakeRestarter((string?)null);
        Assert.Equal(AddressChangeResult.Rejected, Change(restarter).Run("192.168.1.99").ExitCode);
        Assert.Equal(install, File.ReadAllText(_install));
        Assert.Equal(0, restarter.Calls);
    }

    [Fact]
    public void Si_el_servicio_no_arranca_se_deja_todo_como_estaba()
    {
        var files = new[] { _install, _data, _cert, _key }.ToDictionary(f => f, File.ReadAllText);
        var restarter = new FakeRestarter("se ha detenido al arrancar", null);
        var r = Change(restarter).Run("192.168.1.24");
        Assert.Equal(AddressChangeResult.ServiceFailed, r.ExitCode);
        Assert.Contains("como estaba", r.Message, StringComparison.Ordinal);
        Assert.Equal(2, restarter.Calls); // con la IP nueva y, tras restaurar, con la anterior
        foreach (var (file, text) in files) Assert.Equal(text, File.ReadAllText(file));
    }

    [Fact]
    public void La_CA_se_encuentra_tambien_en_la_carpeta_virtual_de_una_app_empaquetada()
    {
        // Así quedó en el PC del usuario: mkcert se ejecutó desde una terminal dentro de una app MSIX.
        var local = Path.Combine(_dir.Path, "Local");
        var virtualRoot = Path.Combine(local, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Local", "mkcert");
        Directory.CreateDirectory(virtualRoot);
        foreach (var f in new[] { "rootCA.pem", "rootCA-key.pem" }) File.Copy(Path.Combine(_caRoot, f), Path.Combine(virtualRoot, f));
        Assert.Equal(virtualRoot, PegasusDeck.Core.Security.MkcertRoot.Default(local));

        // Si también está en la carpeta normal, manda la normal.
        var normal = Path.Combine(local, "mkcert");
        Directory.CreateDirectory(normal);
        foreach (var f in new[] { "rootCA.pem", "rootCA-key.pem" }) File.Copy(Path.Combine(_caRoot, f), Path.Combine(normal, f));
        Assert.Equal(normal, PegasusDeck.Core.Security.MkcertRoot.Default(local));
    }
}
