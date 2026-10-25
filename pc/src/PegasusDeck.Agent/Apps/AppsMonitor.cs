using System.ComponentModel;
using System.Diagnostics;
using PegasusDeck.Agent.Windows;
using PegasusDeck.Core.Apps;
using PegasusDeck.Core.Contract;
using PegasusDeck.Core.Native;

namespace PegasusDeck.Agent.Apps;

/// <summary>
/// Estado de las apps autorizadas. Por ciclo: UNA instantánea de procesos, solo de esta sesión
/// de Windows, con la ruta completa del ejecutable (en caché por proceso); y, solo si alguna app
/// lo necesita, una enumeración de ventanas. "ready" exige el nivel configurado: proceso, o
/// ventana principal real (visible, no de herramientas, no oculta por DWM, con tamaño y título)
/// que responde.
/// </summary>
internal sealed class AppsMonitor
{
    /// <summary>Tras lanzar una app, se considera "iniciándose" como mucho este tiempo.</summary>
    public static readonly TimeSpan StartingWindow = TimeSpan.FromSeconds(30);

    private readonly object _lock = new();
    private readonly Dictionary<string, DateTime> _launchedAt = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Pid, long Start), string?> _paths = [];
    private IReadOnlyList<AppEntry> _apps = [];

    public IReadOnlyList<AppEntry> Apps
    {
        get { lock (_lock) return _apps; }
    }

    public void SetApps(IReadOnlyList<AppEntry> apps)
    {
        lock (_lock) _apps = apps;
    }

    public AppEntry? Find(string id) => Apps.FirstOrDefault(a => a.Id == id);

    public void MarkLaunched(string id)
    {
        lock (_lock) _launchedAt[id] = DateTime.UtcNow;
    }

    /// <summary>Calcula el estado de todas las apps. false si la detección ha fallado (estado desconocido).</summary>
    public bool TrySnapshot(out IReadOnlyList<AppInfo> apps)
    {
        try
        {
            var scan = Scan(Apps, forceWindows: true);
            apps = Apps.Select(a => new AppInfo(a.Id, a.Name, StateOf(a, scan), Closable(a, scan).Count, IsGame(a.ExePath) ? "game" : null)).ToList();
            return true;
        }
#pragma warning disable CA1031 // Cualquier fallo de detección se informa como "desconocido", nunca como "cerrada".
        catch (Exception)
#pragma warning restore CA1031
        {
            apps = [];
            return false;
        }
    }

    public string StateOf(AppEntry app) => StateOf(app, Scan([app]));

    public IntPtr MainWindow(AppEntry app)
    {
        var scan = Scan([app], forceWindows: true);
        foreach (var pid in Pids(app, scan))
        {
            if (scan.Windows.TryGetValue(pid, out var ws))
            {
                var best = ws.OrderByDescending(w => w.Responding).FirstOrDefault();
                if (best.Hwnd != IntPtr.Zero) return best.Hwnd;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Ventanas de la app que se pueden cerrar (ADR-024): las principales de sus procesos en esta sesión.
    /// Del Explorador solo las carpetas abiertas (nunca la barra de tareas ni el escritorio).
    /// </summary>
    public IReadOnlyList<IntPtr> ClosableWindows(AppEntry app) => Closable(app, Scan([app], forceWindows: true));

    /// <summary>Carpetas de juegos de las tiendas habituales (ADR-027): solo para elegir el icono del iPad.</summary>
    private static readonly string[] GameDirs =
    [
        @"\steamapps\common\", @"\Epic Games\", @"\Battle.net\", @"\Call of Duty\", @"\Rockstar Games\",
        @"\Ubisoft Game Launcher\games\", @"\EA Games\", @"\XboxGames\", @"\GOG Galaxy\Games\", @"\Riot Games\",
    ];

    private static readonly string[] Launchers = ["steam.exe", "epicgameslauncher.exe", "battle.net.exe", "launcher.exe", "eadesktop.exe", "riotclientservices.exe", "upc.exe"];

    public static bool IsGame(string exePath) =>
        GameDirs.Any(d => exePath.Contains(d, StringComparison.OrdinalIgnoreCase)) &&
        !Launchers.Contains(Path.GetFileName(exePath).ToLowerInvariant());

    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal) { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    private static List<IntPtr> Closable(AppEntry app, ScanResult scan)
    {
        var explorer = Path.GetFileName(app.ExePath).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
        return [.. Pids(app, scan)
            .SelectMany(pid => scan.Windows.TryGetValue(pid, out var ws) ? ws : [])
            .Select(w => w.Hwnd)
            .Where(h => ClassOf(h) is var c && (explorer ? c == "CabinetWClass" : !ShellClasses.Contains(c)))];
    }

    private static string ClassOf(IntPtr hwnd)
    {
        var buffer = new char[256];
        var n = NativeMethods.GetClassName(hwnd, buffer, buffer.Length);
        return n > 0 ? new string(buffer, 0, n) : "";
    }

    private sealed record ScanResult(
        Dictionary<string, List<int>> PidsByPath, Dictionary<int, List<(IntPtr Hwnd, bool Responding)>> Windows, Dictionary<string, List<int>> PidsByDir);

    /// <summary>Carpeta actual del paquete de una app de Store, con barra final (ADR-030).</summary>
    private static string? PackageDir(AppEntry app) =>
        app.Aumid is { } aumid && PackagedApps.InstallDir(aumid) is { } dir ? Normalize(dir).TrimEnd('\\') + "\\" : null;

    private static IEnumerable<string> PathsOf(AppEntry app) => [app.ExePath, .. app.DetectExePaths ?? []];

    private string StateOf(AppEntry app, ScanResult scan)
    {
        if (app.Aumid is null ? !File.Exists(app.ExePath) : PackageDir(app) is null) return "not_installed";
        var pids = Pids(app, scan);
        if (pids.Count == 0)
        {
            lock (_lock)
            {
                return _launchedAt.TryGetValue(app.Id, out var at) && DateTime.UtcNow - at < StartingWindow ? "starting" : "installed";
            }
        }
        if (app.ReadyLevel == "process") return "ready";
        return pids.Any(pid => scan.Windows.TryGetValue(pid, out var ws) && ws.Any(w => w.Responding)) ? "ready" : "running";
    }

    private static List<int> Pids(AppEntry app, ScanResult scan) =>
        PathsOf(app).SelectMany(p => scan.PidsByPath.TryGetValue(Normalize(p), out var l) ? l : [])
            // App de Store: cualquier proceso de la carpeta de su paquete (los juegos de Xbox arrancan otro .exe).
            .Concat(PackageDir(app) is { } dir && scan.PidsByDir.TryGetValue(dir, out var d) ? d : [])
            .Distinct().ToList();

    private ScanResult Scan(IReadOnlyList<AppEntry> apps, bool forceWindows = false)
    {
        var wanted = apps.SelectMany(PathsOf).Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = wanted.Select(p => Path.GetFileNameWithoutExtension(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dirs = apps.Select(PackageDir).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var byPath = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var byDir = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        using var self = Process.GetCurrentProcess();
        var session = self.SessionId;
        var alive = new HashSet<(int, long)>();

        foreach (var p in Process.GetProcesses()) // una sola instantánea del sistema por ciclo
        {
            using (p)
            {
                if (p.SessionId != session || (!names.Contains(p.ProcessName) && dirs.Count == 0)) continue;
                long start;
                try { start = p.StartTime.Ticks; }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException) { continue; }
                var key = (p.Id, start);
                alive.Add(key);
                string? path;
                lock (_lock)
                {
                    if (!_paths.TryGetValue(key, out path)) _paths[key] = path = ProcessIdentity.ImagePath(p.Id);
                }
                if (path is null) continue;
                var norm = Normalize(path);
                foreach (var dir in dirs.Where(d => norm.StartsWith(d, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!byDir.TryGetValue(dir, out var inDir)) byDir[dir] = inDir = [];
                    inDir.Add(p.Id);
                }
                if (!wanted.Contains(norm)) continue;
                if (!byPath.TryGetValue(norm, out var list)) byPath[norm] = list = [];
                list.Add(p.Id);
            }
        }
        lock (_lock)
        {
            foreach (var dead in _paths.Keys.Where(k => !alive.Contains(k)).ToList()) _paths.Remove(dead);
        }

        var needWindows = forceWindows || apps.Any(a => a.ReadyLevel == "window");
        return new ScanResult(byPath, needWindows ? MainWindowsByProcess() : [], byDir);
    }

    /// <summary>Ventanas principales "de verdad" por proceso (descarta splash de herramientas, ocultas y sin tamaño).</summary>
    private static Dictionary<int, List<(IntPtr Hwnd, bool Responding)>> MainWindowsByProcess()
    {
        var map = new Dictionary<int, List<(IntPtr, bool)>>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.GetWindow(hwnd, NativeMethods.GwOwner) != IntPtr.Zero) return true;
            if ((NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64() & NativeMethods.WsExToolWindow) != 0) return true;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            if (!NativeMethods.GetWindowRect(hwnd, out var r) || r.Right - r.Left <= 0 || r.Bottom - r.Top <= 0) return true;
            if (NativeMethods.GetWindowTextLength(hwnd) == 0) return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (!map.TryGetValue((int)pid, out var list)) map[(int)pid] = list = [];
            list.Add((hwnd, !NativeMethods.IsHungAppWindow(hwnd)));
            return true;
        }, IntPtr.Zero);
        return map;
    }

    private static string Normalize(string path) => Path.GetFullPath(path);
}

/// <summary>Abre o trae al frente una app de la lista blanca. Nunca un shell ni texto del cliente.</summary>
internal static class AppLauncher
{
    /// <summary>ERROR_ELEVATION_REQUIRED: la app pide permisos de administrador.</summary>
    private const int ElevationRequired = 740;

    /// <summary>Lanza la app; devuelve un motivo legible si Windows no la deja abrir.</summary>
    public static string? Launch(AppEntry app)
    {
        // App de Store (ADR-030): activación de apps empaquetadas de Windows, sin shell ni argumentos.
        if (app.Aumid is { } aumid) return PackagedApps.Activate(aumid);
        if (!File.Exists(app.ExePath)) return "el ejecutable autorizado no existe";
        var psi = new ProcessStartInfo
        {
            FileName = app.ExePath,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(app.ExePath)!,
        };
        foreach (var a in app.Args) psi.ArgumentList.Add(a);
        try
        {
            using var _ = Process.Start(psi);
            return null;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == ElevationRequired)
        {
            return "necesita permisos de administrador y Pegasus Deck no abre programas con elevación";
        }
        catch (Win32Exception)
        {
            return "Windows no ha permitido abrirla";
        }
    }

    /// <summary>
    /// Intenta traer la ventana al frente. Windows solo lo permite en ciertos casos (p. ej. si nadie ha usado
    /// el teclado o el ratón del PC en un rato): se devuelve si lo consiguió de verdad.
    /// </summary>
    public static bool BringToFront(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SwRestore);
        NativeMethods.SetForegroundWindow(hwnd);
        return NativeMethods.GetForegroundWindow() == hwnd;
    }
}
