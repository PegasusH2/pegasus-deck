namespace PegasusDeck.Core.Security;

/// <summary>Límite de intentos por clave (p. ej. IP) en una ventana deslizante.</summary>
public sealed class RateLimiter(int maxPerWindow, TimeSpan window, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, Queue<DateTimeOffset>> _hits = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <summary>Claves vigiladas ahora mismo (para comprobar que no crece sin límite).</summary>
    public int KeyCount { get { lock (_lock) return _hits.Count; } }

    /// <summary>Registra un intento y devuelve false si supera el límite.</summary>
    public bool TryAcquire(string key)
    {
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            if (!_hits.TryGetValue(key, out var q)) _hits[key] = q = new Queue<DateTimeOffset>();
            while (q.Count > 0 && now - q.Peek() >= window) q.Dequeue();
            if (q.Count >= maxPerWindow) return false;
            q.Enqueue(now);
            // Limpieza de claves cuyo último intento ya salió de la ventana (no crece sin límite).
            if (_hits.Count > 256)
            {
                foreach (var k in _hits.Where(kv => kv.Value.Count == 0 || now - kv.Value.Last() >= window).Select(kv => kv.Key).ToList())
                {
                    _hits.Remove(k);
                }
            }
            return true;
        }
    }
}
