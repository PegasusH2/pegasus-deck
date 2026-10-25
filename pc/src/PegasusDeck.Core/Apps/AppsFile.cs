// Lista blanca de aplicaciones (ADR-004): %ProgramData%\PegasusDeck\apps.json,
// en la carpeta protegida. Solo la edita un administrador; la PWA solo ve ids.

using System.Text.Json;
using System.Text.RegularExpressions;
using PegasusDeck.Core.Contract;
using PegasusDeck.Core.Storage;

namespace PegasusDeck.Core.Apps;

/// <summary>
/// Una aplicación autorizada. ReadyLevel: "process" (basta con el proceso) o "window"
/// (ventana visible que responde). AlwaysLaunch: abrir siempre una instancia nueva
/// (p. ej. el Explorador, cuyo proceso siempre existe). AllowUnprotectedPath: el
/// administrador acepta expresamente que el ejecutable esté en una carpeta que pueden
/// modificar usuarios sin privilegios (si no, el agente se niega a lanzarlo).
/// DetectExePaths: otros ejecutables que cuentan como "la app abierta" (lanzadores que arrancan
/// otro programa y terminan, p. ej. Update.exe → Discord.exe).
/// Aumid (ADR-030): app de Microsoft Store / MSIX. Se abre con la activación de apps empaquetadas de
/// Windows (sin shell); ExePath es su ejecutable dentro del paquete y solo sirve para saber si está abierta
/// (cuenta cualquier proceso de su carpeta: los juegos de Xbox arrancan otro .exe).
/// </summary>
public sealed record AppEntry(
    string Id,
    string Name,
    string ExePath,
    IReadOnlyList<string> Args,
    string ReadyLevel,
    bool AlwaysLaunch,
    bool AllowUnprotectedPath = false,
    IReadOnlyList<string>? DetectExePaths = null,
    string? Aumid = null);

public static partial class AppsFile
{
    public const string FileName = "apps.json";
    public const int MaxApps = 100;

    private static readonly JsonSerializerOptions Indented = new(JsonDefaults.Options) { WriteIndented = true };

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex IdRegex();

    /// <summary>AppUserModelID de una app empaquetada: Familia_IdEditor!IdApp (sin rutas ni argumentos).</summary>
    [GeneratedRegex(@"^[A-Za-z0-9.\-]{1,100}_[a-z0-9]{13}![A-Za-z0-9._\-]{1,64}$")]
    public static partial Regex AumidRegex();

    private static readonly HashSet<string> BlockedExecutables = new(StringComparer.Ordinal)
    {
        "cmd.exe", "powershell.exe", "powershell_ise.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe",
        "rundll32.exe", "regsvr32.exe", "wsl.exe", "bash.exe", "conhost.exe", "forfiles.exe", "msbuild.exe",
        "msiexec.exe", "schtasks.exe", "at.exe", "certutil.exe", "bitsadmin.exe", "installutil.exe", "regasm.exe",
        "regsvcs.exe", "cmstp.exe", "wmic.exe", "pcalua.exe", "scriptrunner.exe", "hh.exe", "mmc.exe", "runas.exe",
        "sc.exe", "reg.exe", "netsh.exe", "msdt.exe", "control.exe", "odbcconf.exe", "presentationhost.exe",
        "dotnet.exe", "node.exe", "python.exe", "pythonw.exe", "java.exe", "javaw.exe", "wt.exe",
    };

    public static (IReadOnlyList<AppEntry> Apps, IReadOnlyList<string> Errors) Load(string dataDir)
    {
        var path = Path.Combine(dataDir, FileName);
        if (!File.Exists(path)) return ([], []);
        List<AppEntry>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<List<AppEntry>>(File.ReadAllText(path), JsonDefaults.Options);
        }
        catch (JsonException e)
        {
            return ([], [$"{FileName} no es JSON válido: {e.Message}"]);
        }
        return Validate(raw ?? []);
    }

    /// <summary>Descarta (y explica) las entradas que no cumplen las reglas; nunca se ejecuta algo dudoso.</summary>
    public static (IReadOnlyList<AppEntry> Apps, IReadOnlyList<string> Errors) Validate(IEnumerable<AppEntry> entries)
    {
        var ok = new List<AppEntry>();
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries.Take(MaxApps))
        {
            var problem = Check(e);
            if (problem is null && !ids.Add(e.Id)) problem = "id duplicado";
            if (problem is null) ok.Add(e with { Args = e.Args ?? [] });
            else errors.Add($"«{e?.Id}»: {problem}");
        }
        return (ok, errors);
    }

    private static string? PathProblem(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return "debe ser una ruta absoluta";
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "debe ser un .exe";
        if (path.Contains("..", StringComparison.Ordinal) || path.StartsWith(@"\\", StringComparison.Ordinal)) return "no puede ser relativa ni de red";
        return null;
    }

    private static string? Check(AppEntry? e)
    {
        if (e is null) return "entrada vacía";
        if (e.Id is null || !IdRegex().IsMatch(e.Id)) return "id no válido";
        if (string.IsNullOrWhiteSpace(e.Name) || e.Name.Length > 60 || !RequestParser.IsDisplaySafe(e.Name)) return "nombre no válido";
        if (e.ReadyLevel is not ("process" or "window")) return "readyLevel debe ser process o window";
        if (PathProblem(e.ExePath) is { } pathProblem) return $"exePath {pathProblem}";
        if (e.Aumid is not null && !AumidRegex().IsMatch(e.Aumid)) return "aumid no válido";
        if (e.Aumid is not null && (e.Args?.Count ?? 0) > 0) return "una app de Store no admite argumentos";
        if ((e.DetectExePaths?.Count ?? 0) > 5) return "detectExePaths admite como mucho 5 rutas";
        foreach (var d in e.DetectExePaths ?? [])
        {
            if (PathProblem(d) is { } p) return $"detectExePaths {p}";
        }
        var exe = Path.GetFileName(e.ExePath).ToLowerInvariant();
        // Intérpretes y herramientas que ejecutan órdenes u otros programas: abrirían la puerta a ejecución arbitraria.
        if (BlockedExecutables.Contains(exe)) return $"{exe} no está permitido";
        // El Explorador con argumentos abre (y ejecuta) lo que se le indique.
        if (exe == "explorer.exe" && (e.Args?.Count ?? 0) > 0) return "explorer.exe no admite argumentos";
        if ((e.Args?.Count ?? 0) > 10 || (e.Args ?? []).Any(a => a is null || a.Length > 200 || !RequestParser.IsDisplaySafe(a))) return "argumentos no válidos";
        return null;
    }

    /// <summary>Crea un apps.json inicial con las aplicaciones conocidas que existan en este PC.</summary>
    public static IReadOnlyList<AppEntry> CreateDefault(string dataDir)
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new[]
        {
            new AppEntry("obs", "OBS Studio", Path.Combine(pf, @"obs-studio\bin\64bit\obs64.exe"), [], "window", false),
            new AppEntry("chrome", "Google Chrome", Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"), [], "window", false),
            new AppEntry("davinci", "DaVinci Resolve", Path.Combine(pf, @"Blackmagic Design\DaVinci Resolve\Resolve.exe"), [], "window", false),
            new AppEntry("explorer", "Explorador de archivos", Path.Combine(win, "explorer.exe"), [], "process", true),
        };
        var found = candidates.Where(c => File.Exists(c.ExePath)).ToList();
        AtomicFile.WriteAllText(Path.Combine(dataDir, FileName),
            JsonSerializer.Serialize(found, Indented));
        return found;
    }
}
