// Protocolo servicio ↔ agente de sesión por named pipe (ADR-016).
// Una línea JSON por mensaje (UTF-8, máx. 64 KB). El servicio es el servidor.
//
//   Agente → servicio: hello (primero), state, result (respuesta a execute), operation (confirmación posterior)
//   Servicio → agente: hello (lista blanca de apps), apps (lista blanca cambiada, ADR-023), paired (iPad emparejado), execute

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PegasusDeck.Core.Apps;
using PegasusDeck.Core.Contract;

namespace PegasusDeck.Core.Agent;

public static class AgentProtocol
{
    public const int Version = 1;
    public const string DefaultPipeName = "PegasusDeck.Agent";
    public const int MaxLineBytes = 64 * 1024;
}

// --- Agente → servicio ---------------------------------------------------------

public sealed record AgentHello(int Protocol, string AgentVersion, int SessionId)
{
    [JsonPropertyOrder(-1)] public string Type { get; } = "hello";
}

public sealed record AgentStateMessage(
    bool SessionReady, ObsInfo Obs, IReadOnlyList<AppInfo> Apps, string AppsDetection, AudioInfo Audio, MicInfo? Mic = null, MediaInfo? Media = null,
    OutputsInfo? Outputs = null)
{
    [JsonPropertyOrder(-1)] public string Type { get; } = "state";
}

/// <summary>Respuesta inmediata a "execute": completed | accepted | failed.</summary>
public sealed record AgentResult(
    string OperationId,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code,
    string Message)
{
    [JsonPropertyOrder(-1)] public string Type { get; } = "result";

    public static AgentResult Completed(string id, string message) => new(id, "completed", null, message);
    public static AgentResult Accepted(string id, string message) => new(id, "accepted", null, message);
    public static AgentResult Failed(string id, string code, string message) => new(id, "failed", code, message);
}

/// <summary>Confirmación posterior de una acción aceptada: completed | failed.</summary>
public sealed record AgentOperation(
    string OperationId,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code,
    string Message)
{
    [JsonPropertyOrder(-1)] public string Type { get; } = "operation";
}

// --- Servicio → agente ---------------------------------------------------------

public sealed record ServiceHello(int Protocol, IReadOnlyList<AppEntry> Apps)
{
    [JsonPropertyOrder(-1)] public string Type { get; } = "hello";
}

/// <summary>apps.json ha cambiado (menú "Aplicaciones autorizadas…" u otro administrador): nueva lista blanca.</summary>
public sealed record ServiceApps(IReadOnlyList<AppEntry> Apps)
{
    [JsonPropertyOrder(-1)] public string Type { get; } = "apps";
}

/// <summary>Un iPad se ha emparejado con el código (la ventana de emparejamiento lo confirma).</summary>
public sealed record ServicePaired(string DeviceName)
{
    [JsonPropertyOrder(-1)] public string Type { get; } = "paired";
}

/// <summary>Acción ya validada por el servicio; el agente la vuelve a validar.</summary>
public sealed record ExecuteMessage(string OperationId, string Action, IReadOnlyDictionary<string, object> Params)
{
    [JsonPropertyOrder(-1)] public string Type { get; } = "execute";
}

/// <summary>Lectura/escritura de mensajes de una línea sobre un stream (pipe).</summary>
public sealed class LineChannel(Stream stream) : IDisposable
{
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly byte[] _buffer = new byte[8192];
    private readonly MemoryStream _pending = new();
    private int _start;
    private int _end;

    public async Task SendAsync(object message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), JsonDefaults.Options);
        await _write.WaitAsync(ct);
        try
        {
            await stream.WriteAsync(bytes, ct);
            await stream.WriteAsync("\n"u8.ToArray(), ct);
            await stream.FlushAsync(ct);
        }
        finally
        {
            _write.Release();
        }
    }

    /// <summary>Siguiente mensaje (o null si el otro extremo cerró). Lanza si una línea supera el máximo.</summary>
    public async Task<JsonDocument?> ReceiveAsync(CancellationToken ct)
    {
        _pending.SetLength(0);
        while (true)
        {
            if (_start < _end)
            {
                var nl = Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start);
                var take = (nl >= 0 ? nl : _end) - _start;
                _pending.Write(_buffer, _start, take);
                _start += take;
                if (_pending.Length > AgentProtocol.MaxLineBytes) throw new InvalidDataException("Mensaje demasiado grande.");
                if (nl >= 0)
                {
                    _start++; // salta '\n'
                    if (_pending.Length == 0) continue;
                    return JsonDocument.Parse(_pending.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
                }
            }
            _start = 0;
            _end = await stream.ReadAsync(_buffer, ct);
            if (_end == 0) return null;
        }
    }

    public static string? TypeOf(JsonDocument doc) =>
        doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;

    public static T? As<T>(JsonDocument doc) => doc.Deserialize<T>(JsonDefaults.Options);

    public void Dispose()
    {
        _write.Dispose();
        _pending.Dispose();
    }

    /// <summary>Texto seguro para registros (sin saltos ni caracteres de control).</summary>
    public static string Printable(string s) =>
        new(s.Take(200).Select(c => char.IsControl(c) ? ' ' : c).ToArray());

    internal static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
}
