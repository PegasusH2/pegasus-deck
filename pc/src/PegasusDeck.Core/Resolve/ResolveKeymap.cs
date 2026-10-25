using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace PegasusDeck.Core.Resolve;

/// <summary>
/// Atajos del preset de teclado ACTIVO de DaVinci Resolve del usuario (ADR-031), leídos de
/// %APPDATA%\Blackmagic Design\DaVinci Resolve\Preferences: config.user.xml dice qué preset está
/// activo (CustomKeyboardPreset) y keyboard.preset.xml guarda los presets (bloque binario en hex,
/// UTF-16BE big-endian). Solo lectura. Si algo no se puede leer, se usan los atajos por defecto.
/// </summary>
public static partial class ResolveKeymap
{
    /// <summary>Orden → atajos (formato Qt). Una lista vacía = la orden existe pero no tiene atajo.</summary>
    public sealed record Keymap(string? PresetName, IReadOnlyDictionary<string, IReadOnlyList<int>> Keys);

    public static string PreferencesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Blackmagic Design", "DaVinci Resolve", "Preferences");

    [GeneratedRegex("<CustomKeyboardPreset>([^<]*)</CustomKeyboardPreset>")]
    private static partial Regex ActivePresetRegex();

    [GeneratedRegex("<PresetListBA>([0-9a-fA-F]+)</PresetListBA>")]
    private static partial Regex PresetListRegex();

    /// <summary>Lee el preset activo. null si no hay preset personalizado o no se puede leer.</summary>
    public static Keymap? Load(string? preferencesDir = null)
    {
        var dir = preferencesDir ?? PreferencesDir;
        try
        {
            var config = File.ReadAllText(Path.Combine(dir, "config.user.xml"));
            var active = ActivePresetRegex().Match(config) is { Success: true } m ? m.Groups[1].Value.Trim() : null;
            if (string.IsNullOrEmpty(active)) return null;
            var xml = File.ReadAllText(Path.Combine(dir, "keyboard.preset.xml"));
            if (PresetListRegex().Match(xml) is not { Success: true } blob) return null;
            var presets = ParsePresetList(Convert.FromHexString(blob.Groups[1].Value));
            return presets.TryGetValue(active, out var keys) ? new Keymap(active, keys) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>[versión][nº presets] y por preset [nombre][tamaño][datos]. Público para probarlo.</summary>
    public static Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>> ParsePresetList(ReadOnlySpan<byte> data)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>>(StringComparer.Ordinal);
        var i = 4;
        var count = ReadInt(data, ref i);
        for (var p = 0; p < count && p < 64; p++)
        {
            var name = ReadString(data, ref i);
            var size = ReadInt(data, ref i);
            if (name is null || size < 0 || i + size > data.Length) break;
            result[name] = ParsePreset(data.Slice(i, size));
            i += size;
        }
        return result;
    }

    /// <summary>Preset: [?][nº órdenes] y por orden [nombre][nº atajos] y por atajo [nº teclas][teclas].</summary>
    private static Dictionary<string, IReadOnlyList<int>> ParsePreset(ReadOnlySpan<byte> data)
    {
        var map = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        var i = 4;
        var commands = ReadInt(data, ref i);
        for (var c = 0; c < commands && c < 10_000 && i < data.Length; c++)
        {
            var name = ReadString(data, ref i);
            var shortcuts = ReadInt(data, ref i);
            if (name is null || shortcuts is < 0 or > 16) break;
            var keys = new List<int>();
            for (var s = 0; s < shortcuts; s++)
            {
                var seq = ReadInt(data, ref i);
                if (seq is < 0 or > 4) return map;
                for (var k = 0; k < seq; k++)
                {
                    var key = ReadInt(data, ref i);
                    // Solo atajos de una tecla (con modificadores); los de varias pulsaciones se ignoran.
                    if (k == 0 && seq == 1 && (key & 0x01FFFFFF) != 0) keys.Add(key);
                }
            }
            map[name] = keys;
        }
        return map;
    }

    private static int ReadInt(ReadOnlySpan<byte> d, ref int i)
    {
        if (i + 4 > d.Length) return -1;
        var v = BinaryPrimitives.ReadInt32BigEndian(d.Slice(i, 4));
        i += 4;
        return v;
    }

    private static string? ReadString(ReadOnlySpan<byte> d, ref int i)
    {
        var len = ReadInt(d, ref i);
        if (len < 0 || len > 1000 || i + len > d.Length) return null;
        var s = Encoding.BigEndianUnicode.GetString(d.Slice(i, len));
        i += len;
        return s;
    }

    /// <summary>Atajo para una orden: el del preset del usuario; si la orden no aparece en él, el por defecto.</summary>
    public static (int? Key, string Source) Resolve(Keymap? map, ResolveCommand command)
    {
        if (map is null) return (command.DefaultKey, "por defecto");
        if (!map.Keys.TryGetValue(command.ResolveName, out var keys)) return (command.DefaultKey, "por defecto");
        return keys.Count > 0 ? (keys[0], $"preset «{map.PresetName}»") : (null, $"preset «{map.PresetName}»");
    }
}
