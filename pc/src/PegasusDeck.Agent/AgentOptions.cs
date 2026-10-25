using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PegasusDeck.Core.Agent;
using PegasusDeck.Core.Contract;
using PegasusDeck.Core.Storage;

namespace PegasusDeck.Agent;

/// <summary>
/// Configuración del agente en %LOCALAPPDATA%\PegasusDeck\agent.json (del usuario).
/// La contraseña de OBS se guarda cifrada con DPAPI (solo este usuario en este PC puede leerla).
/// </summary>
internal sealed class AgentOptions
{
    /// <summary>No se lee de agent.json (lo puede escribir cualquier proceso del usuario).</summary>
    [JsonIgnore]
    public string PipeName { get; set; } = AgentProtocol.DefaultPipeName;

    /// <summary>
    /// Servicio de confianza: siempre el de la carpeta del agente (%ProgramFiles%\PegasusDeck). No se lee de
    /// agent.json; solo se puede cambiar por línea de órdenes en compilaciones de desarrollo.
    /// </summary>
    [JsonIgnore]
    public string TrustedServicePath { get; set; } = DefaultServicePath;

    public static string DefaultServicePath => Path.Combine(AppContext.BaseDirectory, "PegasusDeck.Service.exe");

    /// <summary>
    /// Servicio que se ejecuta con UAC desde el menú: siempre el instalado junto al agente (nunca una ruta
    /// configurable). Solo en desarrollo (Debug + --insecure-dev) el de --trusted-service.
    /// </summary>
    [JsonIgnore]
    internal string ElevatedServicePath => InsecureDev ? TrustedServicePath : DefaultServicePath;

    /// <summary>Solo desarrollo (--dev-data-dir): la carpeta de datos del servicio de pruebas.</summary>
    [JsonIgnore]
    internal IReadOnlyList<string> ElevatedServiceArgs { get; private set; } = [];

    public int ObsPort { get; set; } = 4455;

    /// <summary>Contraseña de obs-websocket cifrada con DPAPI (base64), o null.</summary>
    public string? ObsPasswordProtected { get; set; }

    /// <summary>
    /// Solo desarrollo/pruebas (línea de órdenes, compilación Debug): permite otra ruta de servicio y no exige que
    /// el servicio esté en la sesión 0.
    /// </summary>
    internal bool InsecureDev { get; set; }

    /// <summary>Solo pruebas: sin icono de bandeja.</summary>
    internal bool Headless { get; set; }

    internal string? ConfigPath { get; set; }

    /// <summary>Solo pruebas: contraseña de OBS en claro (nunca se guarda).</summary>
    internal string? ObsPasswordOverride { get; set; }

    public static string DefaultConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PegasusDeck", "agent.json");

    public string? ObsPassword
    {
        get
        {
            if (ObsPasswordOverride is not null) return ObsPasswordOverride;
            if (string.IsNullOrEmpty(ObsPasswordProtected)) return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ObsPasswordProtected), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception e) when (e is CryptographicException or FormatException)
            {
                return null;
            }
        }
        set => ObsPasswordProtected = string.IsNullOrEmpty(value)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    public static AgentOptions Load(string[] args)
    {
        var configPath = Arg(args, "--config") ?? DefaultConfigPath;
        AgentOptions o;
        try
        {
            o = File.Exists(configPath)
                ? JsonSerializer.Deserialize<AgentOptions>(File.ReadAllText(configPath), JsonDefaults.Options) ?? new AgentOptions()
                : new AgentOptions();
        }
        catch (JsonException)
        {
            o = new AgentOptions();
        }
        o.ConfigPath = configPath;
        if (Arg(args, "--pipe") is { } pipe) o.PipeName = pipe;
        if (Arg(args, "--obs-port") is { } port && int.TryParse(port, out var p)) o.ObsPort = p;
        o.Headless = args.Contains("--headless");
#if DEBUG
        o.InsecureDev = args.Contains("--insecure-dev");
#endif
        if (o.InsecureDev)
        {
            if (Arg(args, "--trusted-service") is { } svc) o.TrustedServicePath = svc;
            if (Arg(args, "--dev-data-dir") is { } dataDir) o.ElevatedServiceArgs = [$"--Pegasus:DataDir={dataDir}", "--Pegasus:UnprotectedDataDir=true"];
            o.ObsPasswordOverride = Environment.GetEnvironmentVariable("PEGASUS_TEST_OBS_PASSWORD");
        }
        return o;
    }

    public void Save()
    {
        AtomicFile.WriteAllText(ConfigPath ?? DefaultConfigPath, JsonSerializer.Serialize(this, JsonDefaults.Options));
    }

    private static string? Arg(string[] args, string name) =>
        args.Where(a => a.StartsWith(name + "=", StringComparison.Ordinal)).Select(a => a[(name.Length + 1)..]).LastOrDefault();
}
