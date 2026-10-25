using System.Security.Cryptography;
using System.Text.Json;
using PegasusDeck.Core.Contract;
using PegasusDeck.Core.Storage;

namespace PegasusDeck.Core.Security;

public enum PairingCheck { Ok, Invalid }

/// <summary>
/// Código de emparejamiento de 6 cifras: lo genera alguien delante del PC
/// ("PegasusDeck.Service pair"), caduca a los 2 minutos, admite 5 intentos y es de
/// un solo uso. En disco solo se guarda su hash con sal (ADR-011).
/// </summary>
public sealed class PairingStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    public const int MaxAttempts = 5;

    private sealed record Pending(string Salt, string CodeHash, DateTimeOffset ExpiresAt, int Attempts);

    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly object _lock = new();

    public PairingStore(string dataDir, TimeProvider? time = null)
    {
        _path = Path.Combine(dataDir, "pairing.json");
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Genera un código nuevo (invalida el anterior) y lo devuelve en claro una sola vez.</summary>
    public string Create()
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        lock (_lock)
        {
            Write(new Pending(salt, Tokens.Hash(salt + code), _time.GetUtcNow() + Lifetime, 0));
        }
        return code;
    }

    public PairingCheck TryConsume(string code)
    {
        lock (_lock)
        {
            var p = Read();
            if (p is null) return PairingCheck.Invalid;
            var now = _time.GetUtcNow();
            // Un archivo con caducidad lejana o intentos fuera de rango no lo ha escrito Create(): se descarta.
            var tampered = p.ExpiresAt > now + Lifetime || p.Attempts is < 0 or > MaxAttempts;
            if (tampered || now > p.ExpiresAt || p.Attempts >= MaxAttempts)
            {
                File.Delete(_path);
                return PairingCheck.Invalid;
            }
            if (Tokens.Matches(p.Salt + code, p.CodeHash))
            {
                File.Delete(_path); // un solo uso
                return PairingCheck.Ok;
            }
            Write(p with { Attempts = p.Attempts + 1 });
            return PairingCheck.Invalid;
        }
    }

    private Pending? Read()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            return JsonSerializer.Deserialize<Pending>(File.ReadAllText(_path), JsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Write(Pending p) => AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(p, JsonDefaults.Options));
}
