using System.Runtime.InteropServices;
using System.Xml.Linq;
using Windows.Management.Deployment;

namespace PegasusDeck.Agent.Apps;

/// <summary>
/// Apps de Microsoft Store / MSIX (ADR-030): listado, carpeta actual del paquete (cambia con cada
/// actualización) y apertura con la activación de apps empaquetadas de Windows (sin shell ni argumentos).
/// </summary>
internal static class PackagedApps
{
    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(30);
    private static readonly Dictionary<string, (string? Dir, DateTime At)> DirCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Lock = new();

    public static string FamilyOf(string aumid) => aumid.Split('!')[0];

    /// <summary>Carpeta instalada actual del paquete de esta app (null si no está instalada para este usuario).</summary>
    public static string? InstallDir(string aumid)
    {
        var family = FamilyOf(aumid);
        lock (Lock)
        {
            if (DirCache.TryGetValue(family, out var c) && DateTime.UtcNow - c.At < CacheTime) return c.Dir;
        }
        string? dir = null;
        try
        {
            dir = new PackageManager().FindPackagesForUser(string.Empty, family).FirstOrDefault()?.InstalledLocation?.Path;
        }
#pragma warning disable CA1031 // Sin acceso al catálogo de paquetes = no instalada (nunca se lanza nada dudoso).
        catch (Exception)
#pragma warning restore CA1031
        {
            dir = null;
        }
        lock (Lock) DirCache[family] = (dir, DateTime.UtcNow);
        return dir;
    }

    /// <summary>Abre la app por su AUMID. null si Windows la ha abierto; si no, el motivo.</summary>
    public static string? Activate(string aumid)
    {
        try
        {
            var manager = (IApplicationActivationManager)new ApplicationActivationManager();
            try
            {
                var hr = manager.ActivateApplication(aumid, null, 0, out _);
                return hr == 0 ? null : $"Windows no ha podido abrirla (0x{hr:X8})";
            }
            finally
            {
                Marshal.FinalReleaseComObject(manager);
            }
        }
        catch (COMException e)
        {
            return $"Windows no ha podido abrirla (0x{e.HResult:X8})";
        }
    }

    /// <summary>Apps de la Store/MSIX instaladas para este usuario que aparecen en el menú Inicio.</summary>
    public static IReadOnlyList<InstalledProgram> Scan()
    {
        var found = new List<InstalledProgram>();
        try
        {
            foreach (var pkg in new PackageManager().FindPackagesForUser(string.Empty))
            {
                try
                {
                    if (pkg.IsFramework || pkg.IsResourcePackage || pkg.SignatureKind == global::Windows.ApplicationModel.PackageSignatureKind.System) continue;
                    var dir = pkg.InstalledLocation?.Path;
                    if (dir is null) continue;
                    var executables = Executables(dir);
                    foreach (var entry in pkg.GetAppListEntriesAsync().AsTask().GetAwaiter().GetResult())
                    {
                        var aumid = entry.AppUserModelId;
                        var appId = aumid.Contains('!') ? aumid[(aumid.IndexOf('!') + 1)..] : "";
                        if (!executables.TryGetValue(appId, out var exe)) continue;
                        var path = Path.Combine(dir, exe);
                        if (File.Exists(path)) found.Add(new InstalledProgram(entry.DisplayInfo.DisplayName, path, aumid));
                    }
                }
#pragma warning disable CA1031 // Un paquete raro no impide listar los demás.
                catch (Exception)
#pragma warning restore CA1031
                {
                }
            }
        }
        catch (COMException)
        {
            // Sin catálogo de paquetes: quedan los programas del menú Inicio.
        }
        return found;
    }

    /// <summary>Id de app → ejecutable relativo, según el AppxManifest.xml del paquete.</summary>
    private static Dictionary<string, string> Executables(string dir)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var manifest = Path.Combine(dir, "AppxManifest.xml");
        if (!File.Exists(manifest)) return map;
        foreach (var app in XDocument.Load(manifest).Descendants().Where(e => e.Name.LocalName == "Application"))
        {
            var id = (string?)app.Attribute("Id");
            var exe = (string?)app.Attribute("Executable");
            if (id is not null && exe is not null && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) map[id] = exe;
        }
        return map;
    }

#pragma warning disable SYSLIB1096 // COM clásico: una sola llamada.
    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager;

    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string? arguments, int options, out uint processId);
    }
#pragma warning restore SYSLIB1096
}
