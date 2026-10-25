// Análisis estricto de lo que envía la PWA: tipos exactos, sin campos extra y con
// límites de longitud (mismas reglas que contracts/schemas y la PWA).

using System.Text.Json;
using System.Text.RegularExpressions;

namespace PegasusDeck.Core.Contract;

public readonly record struct Parsed<T>(T? Value, string? Error) where T : class
{
    public bool Ok => Value is not null;
}

internal static class Parsed
{
    public static Parsed<T> Fail<T>(string error) where T : class => new(null, error);
    public static Parsed<T> Success<T>(T value) where T : class => new(value, null);
}

public sealed record PairRequest(string Code, string DeviceName);

/// <summary>Valor de parámetro: string, double o bool.</summary>
/// <summary>ConfirmToken: segundo paso de una acción destructiva (ADR-024).</summary>
public sealed record ActionRequest(string Type, IReadOnlyDictionary<string, object> Params, string? ConfirmToken = null);

public static partial class RequestParser
{
    public const int MaxParams = 10;
    public const int MaxStringParam = 200;

    [GeneratedRegex("^[0-9]{6}$")]
    private static partial Regex PairCodeRegex();

    [GeneratedRegex(@"^[a-z]+(\.[a-zA-Z]+)+$")]
    private static partial Regex ActionTypeRegex();

    [GeneratedRegex("^[A-Za-z0-9_-]{20,64}$")]
    private static partial Regex ConfirmTokenRegex();

    public static Parsed<PairRequest> ParsePair(JsonElement e)
    {
        if (!OnlyProperties(e, "code", "deviceName")) return Parsed.Fail<PairRequest>("Campos no válidos.");
        var code = GetString(e, "code");
        var name = GetString(e, "deviceName")?.Trim();
        if (code is null || !PairCodeRegex().IsMatch(code)) return Parsed.Fail<PairRequest>("El código tiene 6 cifras.");
        if (string.IsNullOrEmpty(name) || name.Length > 40 || !IsDisplaySafe(name)) return Parsed.Fail<PairRequest>("Nombre de dispositivo no válido.");
        return Parsed.Success(new PairRequest(code, name));
    }

    public static Parsed<ActionRequest> ParseAction(JsonElement e)
    {
        if (!OnlyProperties(e, "type", "params", "confirmToken")) return Parsed.Fail<ActionRequest>("Campos no válidos.");
        string? confirmToken = null;
        if (e.TryGetProperty("confirmToken", out var ct))
        {
            confirmToken = ct.ValueKind == JsonValueKind.String ? ct.GetString() : null;
            if (confirmToken is null || !ConfirmTokenRegex().IsMatch(confirmToken)) return Parsed.Fail<ActionRequest>("Confirmación no válida.");
        }
        var type = GetString(e, "type");
        if (type is null || type.Length > 60 || !ActionTypeRegex().IsMatch(type)) return Parsed.Fail<ActionRequest>("Tipo de acción no válido.");
        if (!e.TryGetProperty("params", out var p) || p.ValueKind != JsonValueKind.Object) return Parsed.Fail<ActionRequest>("Faltan los parámetros.");

        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var prop in p.EnumerateObject())
        {
            if (values.Count >= MaxParams) return Parsed.Fail<ActionRequest>("Demasiados parámetros.");
            object? v = prop.Value.ValueKind switch
            {
                JsonValueKind.String when prop.Value.GetString()!.Length <= MaxStringParam => prop.Value.GetString()!,
                JsonValueKind.Number => prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
            if (v is null) return Parsed.Fail<ActionRequest>($"Parámetro «{prop.Name}» no válido.");
            values[prop.Name] = v;
        }
        return Parsed.Success(new ActionRequest(type, values, confirmToken));
    }

    /// <summary>Primer mensaje del WebSocket: {"type":"auth","token":"..."}.</summary>
    public static string? ParseAuthToken(JsonElement e)
    {
        if (!OnlyProperties(e, "type", "token") || GetString(e, "type") != "auth") return null;
        var token = GetString(e, "token");
        return token is { Length: > 0 and <= 200 } ? token : null;
    }

    /// <summary>Sin caracteres de control ni de formato (ANSI, CR, BiDi U+202E…): el nombre se muestra en la consola del PC.</summary>
    public static bool IsDisplaySafe(string s) => s.All(c => char.GetUnicodeCategory(c) is not (
        System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format or
        System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator));

    private static bool OnlyProperties(JsonElement e, params string[] allowed)
    {
        if (e.ValueKind != JsonValueKind.Object) return false;
        foreach (var prop in e.EnumerateObject())
        {
            if (Array.IndexOf(allowed, prop.Name) < 0) return false;
        }
        return true;
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
