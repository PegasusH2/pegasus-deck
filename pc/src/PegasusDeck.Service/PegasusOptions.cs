using System.Net;
using Microsoft.Extensions.Options;

namespace PegasusDeck.Service;

/// <summary>Configuración (sección "Pegasus" de appsettings.json o variables Pegasus__*).</summary>
public sealed class PegasusOptions
{
    public const string Section = "Pegasus";

    /// <summary>Datos: dispositivos, código de emparejamiento, logs. Por defecto %ProgramData%\PegasusDeck.</summary>
    public string DataDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PegasusDeck");

    public int Port { get; set; } = 8443;

    /// <summary>IP(s) de la LAN en las que escuchar. Nunca 0.0.0.0: el servicio no debe aparecer en otras redes.</summary>
    public List<string> ListenAddresses { get; set; } = [];

    /// <summary>Nombres/IP con los que la PWA llega al PC (defensa contra DNS rebinding).</summary>
    public List<string> AllowedHosts { get; set; } = [];

    /// <summary>Certificado PEM firmado por la CA local (mkcert).</summary>
    public string? CertificatePath { get; set; }

    public string? CertificateKeyPath { get; set; }

    /// <summary>Solo pruebas: certificado autofirmado generado al arrancar.</summary>
    public bool SelfSignedCertificate { get; set; }

    /// <summary>Solo pruebas y desarrollo: HTTP en 127.0.0.1, sin TLS.</summary>
    public bool DevelopmentHttp { get; set; }

    /// <summary>Carpeta con la PWA compilada (pwa/dist). Por defecto wwwroot junto al ejecutable.</summary>
    public string? WebRoot { get; set; }

    public int HeartbeatSeconds { get; set; } = 5;

    /// <summary>Nombre del named pipe con el agente de sesión.</summary>
    public string AgentPipeName { get; set; } = Core.Agent.AgentProtocol.DefaultPipeName;

    /// <summary>
    /// Ejecutable del agente en el que se confía (por defecto, junto al servicio en %ProgramFiles%).
    /// "*" acepta cualquiera: solo en desarrollo (con UnprotectedDataDir).
    /// </summary>
    public string TrustedAgentPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "PegasusDeck.Agent.exe");

    /// <summary>Intentos de emparejamiento por minuto y por IP (además de los 5 por código).</summary>
    public int PairAttemptsPerMinute { get; set; } = 3;

    /// <summary>Solo desarrollo/pruebas: no exige la ACL protegida en DataDir.</summary>
    public bool UnprotectedDataDir { get; set; }

    public string Scheme => DevelopmentHttp ? "http" : "https";

    /// <summary>Orígenes exactos permitidos: esquema://host:puerto de cada host autorizado.</summary>
    public IReadOnlySet<string> AllowedOrigins()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in AllowedHosts)
        {
            var h = IPAddress.TryParse(host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{host}]" : host;
            set.Add($"{Scheme}://{h}:{Port}");
            if ((Scheme == "https" && Port == 443) || (Scheme == "http" && Port == 80)) set.Add($"{Scheme}://{h}");
        }
        return set;
    }
}

public sealed class PegasusOptionsValidator : IValidateOptions<PegasusOptions>
{
    public ValidateOptionsResult Validate(string? name, PegasusOptions options)
    {
        var o = options;
        var errors = new List<string>();
#if !DEBUG
        // En la versión instalable no existen las opciones de desarrollo: cualquier usuario podría
        // lanzar el ejecutable con ellas y hacerse pasar por el servicio (revisión de seguridad, fase 3).
        if (o.DevelopmentHttp || o.SelfSignedCertificate || o.UnprotectedDataDir || o.TrustedAgentPath == "*")
        {
            errors.Add("Las opciones de desarrollo no están disponibles en esta compilación.");
        }
#endif
        if (o.Port is < 1 or > 65535) errors.Add("Pegasus:Port fuera de rango.");
        if (o.AllowedHosts.Count == 0) errors.Add("Pegasus:AllowedHosts no puede estar vacío (IP o nombre del PC en la LAN).");
        if (o.TrustedAgentPath == "*" && !o.UnprotectedDataDir)
        {
            errors.Add("Pegasus:TrustedAgentPath=\"*\" solo se permite en desarrollo (UnprotectedDataDir).");
        }
        if (o.UnprotectedDataDir && !o.DevelopmentHttp && !o.SelfSignedCertificate)
        {
            errors.Add("Pegasus:UnprotectedDataDir solo se permite en desarrollo (DevelopmentHttp o SelfSignedCertificate).");
        }
        if (!o.DevelopmentHttp)
        {
            if (o.ListenAddresses.Count == 0) errors.Add("Pegasus:ListenAddresses debe indicar la IP de la LAN del PC.");
            foreach (var a in o.ListenAddresses)
            {
                if (!IPAddress.TryParse(a, out var ip)) errors.Add($"Dirección no válida: {a}");
                else if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) errors.Add("No se permite escuchar en todas las interfaces (0.0.0.0 / ::).");
            }
            if (!o.SelfSignedCertificate && (string.IsNullOrWhiteSpace(o.CertificatePath) || string.IsNullOrWhiteSpace(o.CertificateKeyPath)))
            {
                errors.Add("Falta el certificado HTTPS (Pegasus:CertificatePath y Pegasus:CertificateKeyPath).");
            }
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
