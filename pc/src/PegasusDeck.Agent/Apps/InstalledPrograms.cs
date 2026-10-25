using System.Runtime.InteropServices;
using PegasusDeck.Core.Apps;

namespace PegasusDeck.Agent.Apps;

/// <summary>Programa instalado: del menú Inicio, o app de Microsoft Store con su AUMID (ADR-030).</summary>
internal sealed record InstalledProgram(string Name, string ExePath, string? Aumid = null);

internal static class InstalledPrograms
{
    private static readonly string[] Skip = ["unins", "uninstall", "desinstal", "setup", "update", "readme", "help", "ayuda"];

    /// <summary>Accesos directos del menú Inicio (todos los usuarios y el actual) que apuntan a un .exe autorizable.</summary>
    public static IReadOnlyList<InstalledProgram> Scan()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        };
        var found = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return [];
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            foreach (var root in roots.Where(Directory.Exists))
            {
                foreach (var lnk in SafeFiles(root))
                {
                    var name = Path.GetFileNameWithoutExtension(lnk);
                    if (Skip.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
                    var target = Target(shell!, lnk);
                    if (target is null || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(target)) continue;
                    if (found.ContainsKey(target)) continue;
                    // Las mismas reglas que al guardar: si no se podría autorizar, no se ofrece.
                    if (AppsEditor.Prepare([], target, name, allowUnprotectedPath: true).Entry is null) continue;
                    found[target] = new InstalledProgram(AppsEditor.CleanName(name), target);
                }
            }
        }
        catch (COMException)
        {
            // Sin WScript.Shell: queda "Buscar .exe…".
        }
        finally
        {
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
        return [.. found.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static List<string> SafeFiles(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? Target(object shell, string lnk)
    {
        object? shortcut = null;
        try
        {
            shortcut = shellTypeInvoke(shell, "CreateShortcut", lnk);
            return shortcut?.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;
        }
        catch (Exception e) when (e is COMException or System.Reflection.TargetInvocationException)
        {
            return null;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
        }

        static object? shellTypeInvoke(object target, string method, string arg) =>
            target.GetType().InvokeMember(method, System.Reflection.BindingFlags.InvokeMethod, null, target, [arg]);
    }
}
