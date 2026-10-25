// Contrato PWA ↔ PC, protocolo 2. Fuente: contracts/schemas (ADR-007).
// Los nombres se serializan en camelCase; ver JsonDefaults.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace PegasusDeck.Core.Contract;

public static class Protocol
{
    public const int Version = 2;
    public const string ServiceName = "pegasus-deck";
    /// <summary>Código de cierre del WebSocket cuando falla la autenticación.</summary>
    public const int WsCloseAuth = 4401;
}

public static class ErrorCodes
{
    public const string AuthRequired = "AUTH_REQUIRED";
    public const string OriginForbidden = "ORIGIN_FORBIDDEN";
    public const string HostForbidden = "HOST_FORBIDDEN";
    public const string RateLimited = "RATE_LIMITED";
    public const string PairingInvalid = "PAIRING_INVALID";
    public const string BadJson = "BAD_JSON";
    public const string BadParams = "BAD_PARAMS";
    public const string UnknownAction = "UNKNOWN_ACTION";
    public const string NoSession = "NO_SESSION";
    public const string ObsNotReady = "OBS_NOT_READY";
    public const string AppNotAllowed = "APP_NOT_ALLOWED";
    public const string AppNotFound = "APP_NOT_FOUND";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string Timeout = "TIMEOUT";
    public const string Busy = "BUSY";
    public const string Internal = "INTERNAL";
    public const string ConfirmationInvalid = "CONFIRMATION_INVALID";
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}

public sealed record ServiceInfo(string Name, string Version);

public sealed record HealthResponse(int Protocol, ServiceInfo Service);

public sealed record ErrorResponse(string Code, string Message)
{
    [JsonPropertyOrder(-1)] public bool Ok { get; } = false;
}

public sealed record PairSuccess(string DeviceId, string Token)
{
    [JsonPropertyOrder(-1)] public bool Ok { get; } = true;
}

public sealed record CompletedResponse(string Message)
{
    [JsonPropertyOrder(-2)] public bool Ok { get; } = true;
    [JsonPropertyOrder(-1)] public string Status { get; } = "completed";
}

/// <summary>Acción destructiva: no se ha ejecutado; hay que repetirla con este token (ADR-024).</summary>
public sealed record ConfirmRequiredResponse(string ConfirmToken, int ExpiresInSec, string Message)
{
    [JsonPropertyOrder(-2)] public bool Ok { get; } = true;
    [JsonPropertyOrder(-1)] public string Status { get; } = "confirm_required";
}

public sealed record AcceptedResponse(
    string OperationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message)
{
    [JsonPropertyOrder(-2)] public bool Ok { get; } = true;
    [JsonPropertyOrder(-1)] public string Status { get; } = "accepted";
}

// --- Instantánea -------------------------------------------------------------

public sealed record WindowsInfo(bool Booted, string Hostname, long UptimeSec);

public sealed record SessionInfo(bool Ready);

public sealed record ObsInput(string Name, bool Muted);

/// <summary>Estado de OBS: unavailable | connecting | ready | auth_error | incompatible.</summary>
public sealed record ObsInfo(
    string State,
    bool Recording,
    bool RecordingPaused,
    bool Streaming,
    string? Scene,
    IReadOnlyList<string> Scenes,
    IReadOnlyList<ObsInput> Inputs)
{
    public static ObsInfo Unavailable { get; } = new("unavailable", false, false, false, null, [], []);
}

/// <summary>Estado de una app: not_installed | installed | starting | running | ready.</summary>
public sealed record AppInfo(
    string Id,
    string Name,
    string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Windows = null, // ventanas que se pueden cerrar (ADR-024)
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Kind = null); // "game" (ADR-027)

/// <summary>Salida de audio predeterminada de Windows. State: ready | unavailable.</summary>
public sealed record AudioInfo(string State, bool Muted, int Volume)
{
    public static AudioInfo Unavailable { get; } = new("unavailable", false, 0);
}

/// <summary>Micrófono predeterminado de Windows (ADR-022). State: ready | unavailable.</summary>
public sealed record MicInfo(string State, bool Muted)
{
    public static MicInfo Unavailable { get; } = new("unavailable", false);
}

/// <summary>Una salida de audio activa (ADR-035). Id: opaco y estable (hash del identificador de Windows).</summary>
public sealed record OutputDevice(string Id, string Name, bool Default);

/// <summary>Salidas de audio activas de Windows y cuál es la predeterminada (ADR-035). State: ready | unavailable.</summary>
public sealed record OutputsInfo(string State, IReadOnlyList<OutputDevice> Devices)
{
    public static OutputsInfo Unavailable { get; } = new("unavailable", []);

    /// <summary>Igualdad por contenido (los records comparan las listas por referencia).</summary>
    public static bool Same(OutputsInfo? a, OutputsInfo? b) =>
        a is null || b is null ? a == b : a.State == b.State && a.Devices.SequenceEqual(b.Devices);
}

/// <summary>Reproducción multimedia de Windows (ADR-022). State: playing | paused | stopped | none | unavailable.</summary>
public sealed record MediaInfo(string State)
{
    public static MediaInfo Unavailable { get; } = new("unavailable");
}

public static class AppsDetection
{
    public const string Ok = "ok";
    public const string Unavailable = "unavailable";
}

public sealed record Snapshot(
    int Protocol,
    ServiceInfo Service,
    WindowsInfo Windows,
    SessionInfo Session,
    ObsInfo Obs,
    IReadOnlyList<AppInfo> Apps,
    string AppsDetection,
    AudioInfo Audio,
    MicInfo? Mic = null,
    MediaInfo? Media = null,
    OutputsInfo? Outputs = null);

// --- Mensajes del WebSocket (servidor → cliente) --------------------------------

public sealed record SnapshotMessage(string Type, Snapshot Data)
{
    public static SnapshotMessage Ready(Snapshot s) => new("ready", s);
    public static SnapshotMessage Update(Snapshot s) => new("snapshot", s);
}

public sealed record OperationResult(
    string OperationId,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code,
    string Message);

public sealed record OperationMessage(OperationResult Data)
{
    [JsonPropertyOrder(-1)] public string Type { get; } = "operation";
}

public sealed record ErrorMessage(string Code, string Message)
{
    [JsonPropertyOrder(-1)] public string Type { get; } = "error";
}
