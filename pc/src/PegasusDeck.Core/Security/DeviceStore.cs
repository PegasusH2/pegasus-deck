using System.Text.Json;
using PegasusDeck.Core.Contract;
using PegasusDeck.Core.Storage;

namespace PegasusDeck.Core.Security;

public sealed record Device(string DeviceId, string Name, string TokenHash, DateTimeOffset PairedAt);

/// <summary>
/// Dispositivos emparejados en devices.json (solo hashes de token). Se relee si otro
/// proceso lo cambia (p. ej. "devices revoke" desde la línea de comandos).
/// </summary>
public sealed class DeviceStore
{
    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private List<Device> _devices = [];
    private DateTime _loadedStamp = DateTime.MinValue;

    public DeviceStore(string dataDir, TimeProvider? time = null)
    {
        _path = Path.Combine(dataDir, "devices.json");
        _time = time ?? TimeProvider.System;
    }

    public (Device Device, string Token) Add(string name)
    {
        lock (_lock)
        {
            Reload();
            var token = Tokens.NewToken();
            var device = new Device(Tokens.NewDeviceId(), name, Tokens.Hash(token), _time.GetUtcNow());
            _devices = [.. _devices, device];
            Save();
            return (device, token);
        }
    }

    public Device? FindByToken(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 200) return null;
        lock (_lock)
        {
            Reload();
            // Se recorren todos para no filtrar por tiempo cuál coincide.
            Device? found = null;
            foreach (var d in _devices)
            {
                if (Tokens.Matches(token, d.TokenHash)) found = d;
            }
            return found;
        }
    }

    public bool Exists(string deviceId)
    {
        lock (_lock)
        {
            Reload();
            return _devices.Any(d => d.DeviceId == deviceId);
        }
    }

    public IReadOnlyList<Device> List()
    {
        lock (_lock)
        {
            Reload();
            return [.. _devices];
        }
    }

    public bool Revoke(string deviceId)
    {
        lock (_lock)
        {
            Reload();
            var before = _devices.Count;
            _devices = _devices.Where(d => d.DeviceId != deviceId).ToList();
            if (_devices.Count == before) return false;
            Save();
            return true;
        }
    }

    private void Reload()
    {
        if (!File.Exists(_path))
        {
            _devices = [];
            _loadedStamp = DateTime.MinValue;
            return;
        }
        var stamp = File.GetLastWriteTimeUtc(_path);
        if (stamp == _loadedStamp) return;
        _devices = JsonSerializer.Deserialize<List<Device>>(File.ReadAllText(_path), JsonDefaults.Options) ?? [];
        _loadedStamp = stamp;
    }

    private void Save()
    {
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_devices, JsonDefaults.Options));
        _loadedStamp = File.GetLastWriteTimeUtc(_path);
    }
}
