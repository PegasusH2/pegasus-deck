namespace PegasusDeck.Core.Security;

/// <summary>
/// Carpeta de la CA de mkcert del usuario (rootCA.pem + rootCA-key.pem), la que confía el iPad (ADR-034).
/// Si mkcert se ejecutó desde una app empaquetada (MSIX, p. ej. una terminal dentro de otra app), Windows guardó la CA
/// en la carpeta virtual de ese paquete: %LOCALAPPDATA%\Packages\&lt;app&gt;\LocalCache\Local\mkcert, que fuera de esa
/// app no se ve en %LOCALAPPDATA%\mkcert. Se busca en este orden: CAROOT, la carpeta normal y las virtuales.
/// </summary>
public static class MkcertRoot
{
    public static string Default(string? localAppData = null)
    {
        var candidates = Candidates(localAppData).ToList();
        return candidates.FirstOrDefault(HasCa) ?? candidates[0];
    }

    public static IEnumerable<string> Candidates(string? localAppData = null)
    {
        if (Environment.GetEnvironmentVariable("CAROOT") is { Length: > 0 } env) yield return env;
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "mkcert");
        var packages = Path.Combine(localAppData, "Packages");
        string[] dirs;
        try
        {
            dirs = Directory.Exists(packages) ? Directory.GetDirectories(packages) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            dirs = [];
        }
        foreach (var dir in dirs.Order(StringComparer.OrdinalIgnoreCase)) yield return Path.Combine(dir, "LocalCache", "Local", "mkcert");
    }

    public static bool HasCa(string dir) =>
        File.Exists(Path.Combine(dir, "rootCA.pem")) && File.Exists(Path.Combine(dir, "rootCA-key.pem"));
}
