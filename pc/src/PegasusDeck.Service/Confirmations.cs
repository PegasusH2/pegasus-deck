using System.Collections.Concurrent;
using PegasusDeck.Core.Contract;
using PegasusDeck.Core.Security;

namespace PegasusDeck.Service;

/// <summary>
/// Segundo paso de las acciones destructivas (ADR-024): la primera petición no ejecuta nada y
/// devuelve un token de un solo uso (15 s) ligado al dispositivo, al tipo y a los parámetros;
/// solo una segunda petición con ese token ejecuta la acción. Así una única petición falsificada
/// o repetida no puede apagar el PC ni cerrar apps.
/// </summary>
public sealed class Confirmations(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(15);
    private const int MaxPending = 64;

    private readonly ConcurrentDictionary<string, (string Key, DateTimeOffset Expires)> _pending = new(StringComparer.Ordinal);

    public string Issue(string deviceId, ActionRequest request)
    {
        var now = time.GetUtcNow();
        foreach (var (token, p) in _pending)
        {
            if (p.Expires <= now) _pending.TryRemove(token, out _);
        }
        if (_pending.Count >= MaxPending) _pending.Clear(); // nunca crece sin límite
        var t = Tokens.NewToken();
        _pending[t] = (KeyOf(deviceId, request), now + Lifetime);
        return t;
    }

    /// <summary>true si el token existe, no ha caducado y es para este dispositivo y esta misma acción. Se consume siempre.</summary>
    public bool Consume(string token, string deviceId, ActionRequest request) =>
        _pending.TryRemove(token, out var p) && p.Expires > time.GetUtcNow() && p.Key == KeyOf(deviceId, request);

    private static string KeyOf(string deviceId, ActionRequest r) =>
        $"{deviceId}\n{r.Type}\n{string.Join("\n", r.Params.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"))}";
}
