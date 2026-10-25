// Añadir y quitar apps de la lista blanca (ADR-023). Lo usan la orden local
// "PegasusDeck.Service apps …" (con permisos de administrador) y, para avisar antes
// de pedir permiso, el menú "Aplicaciones autorizadas…" del agente. Las reglas son
// las de siempre (AppsFile.Validate): nada de intérpretes, rutas relativas o de red.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PegasusDeck.Core.Contract;
using PegasusDeck.Core.Storage;

namespace PegasusDeck.Core.Apps;

public sealed record AppsEditResult(bool Ok, string Message, string? Id = null);

public static class AppsEditor
{
    private static readonly JsonSerializerOptions Indented = new(JsonDefaults.Options) { WriteIndented = true };

    /// <summary>Entrada nueva para un ejecutable (sin escribir nada). Error si no se puede autorizar.</summary>
    public static (AppEntry? Entry, string? Error) Prepare(IReadOnlyList<AppEntry> current, string exePath, string? name, bool allowUnprotectedPath, string? aumid = null)
    {
        if (aumid is not null && current.FirstOrDefault(a => string.Equals(a.Aumid, aumid, StringComparison.OrdinalIgnoreCase)) is { } sameApp)
        {
            return (null, $"Ya está autorizada como «{sameApp.Name}».");
        }
        if (string.IsNullOrWhiteSpace(exePath) || !Path.IsPathFullyQualified(exePath)) return (null, "Elige un ejecutable (.exe) con su ruta completa.");
        var path = Path.GetFullPath(exePath);
        if (!File.Exists(path)) return (null, "El ejecutable no existe.");
        if (current.FirstOrDefault(a => string.Equals(a.ExePath, path, StringComparison.OrdinalIgnoreCase)) is { } dup)
        {
            return (null, $"Ya está autorizada como «{dup.Name}».");
        }
        var display = CleanName(string.IsNullOrWhiteSpace(name) ? SuggestName(path) : name);
        var explorer = Path.GetFileName(path).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
        var entry = new AppEntry(NewId(display, current.Select(a => a.Id)), display, path, [],
            explorer ? "process" : "window", AlwaysLaunch: explorer, AllowUnprotectedPath: allowUnprotectedPath, Aumid: aumid);
        var (_, errors) = AppsFile.Validate([entry]);
        return errors.Count > 0 ? (null, $"No se puede autorizar: {errors[0].Split(": ", 2)[^1]}.") : (entry, null);
    }

    public static AppsEditResult Add(string dataDir, string exePath, string? name, bool allowUnprotectedPath, string? aumid = null)
    {
        if (ReadRaw(dataDir) is not { } list) return new(false, $"{AppsFile.FileName} no es válido: corrígelo antes de añadir apps.");
        var (entry, error) = Prepare(list, exePath, name, allowUnprotectedPath, aumid);
        if (entry is null) return new(false, error!);
        list.Add(entry);
        Save(dataDir, list);
        return new(true, $"«{entry.Name}» autorizada.", entry.Id);
    }

    public static AppsEditResult Remove(string dataDir, string id)
    {
        if (ReadRaw(dataDir) is not { } list) return new(false, $"{AppsFile.FileName} no es válido: corrígelo antes de quitar apps.");
        var app = list.FirstOrDefault(a => a?.Id == id);
        if (app is null) return new(false, $"No hay ninguna app con id «{id}».");
        list.Remove(app);
        Save(dataDir, list);
        return new(true, $"«{app.Name}» ya no está autorizada.", id);
    }

    /// <summary>Todas las entradas tal como están (también las que no son válidas: no se pierden al guardar).</summary>
    private static List<AppEntry>? ReadRaw(string dataDir)
    {
        var path = Path.Combine(dataDir, AppsFile.FileName);
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<AppEntry>>(File.ReadAllText(path), JsonDefaults.Options) ?? [];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Save(string dataDir, List<AppEntry> list) =>
        AtomicFile.WriteAllText(Path.Combine(dataDir, AppsFile.FileName), JsonSerializer.Serialize(list, Indented));

    /// <summary>Nombre del programa según su ficha (descripción o producto) o, si no tiene, el del archivo.</summary>
    public static string SuggestName(string exePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            foreach (var candidate in new[] { info.FileDescription, info.ProductName })
            {
                if (!string.IsNullOrWhiteSpace(candidate)) return CleanName(candidate);
            }
        }
        catch (FileNotFoundException)
        {
            // se usa el nombre del archivo
        }
        return CleanName(Path.GetFileNameWithoutExtension(exePath));
    }

    public static string CleanName(string name)
    {
        var clean = new string(name.Where(c => RequestParser.IsDisplaySafe(c.ToString())).ToArray()).Trim();
        if (clean.Length > 60) clean = clean[..60].Trim();
        return clean.Length == 0 ? "Aplicación" : clean;
    }

    /// <summary>Id a partir del nombre: minúsculas sin tildes, guiones, máx. 40, único.</summary>
    public static string NewId(string name, IEnumerable<string> taken)
    {
        // Tabla propia: con InvariantGlobalization no se puede descomponer las tildes con Normalize.
        const string from = "áàäâãåéèëêíìïîóòöôõúùüûñçý";
        const string to = "aaaaaaeeeeiiiiooooouuuuncy";
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            var l = char.ToLowerInvariant(c);
            var i = from.IndexOf(l, StringComparison.Ordinal);
            if (i >= 0) l = to[i];
            sb.Append(l is >= 'a' and <= 'z' or >= '0' and <= '9' ? l : '-');
        }
        var slug = string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length > 36) slug = slug[..36].TrimEnd('-');
        if (slug.Length == 0) slug = "app";
        var used = taken.ToHashSet(StringComparer.Ordinal);
        var id = slug;
        for (var n = 2; used.Contains(id); n++) id = $"{slug}-{n}";
        return id;
    }
}
