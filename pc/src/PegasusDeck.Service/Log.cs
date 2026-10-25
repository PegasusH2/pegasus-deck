namespace PegasusDeck.Service;

/// <summary>
/// Mensajes de registro del servicio. Nunca incluyen tokens, códigos de
/// emparejamiento ni cuerpos de petición.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(1, LogLevel.Warning, "Petición rechazada: Host no autorizado ({Host})")]
    public static partial void HostRejected(ILogger logger, string host);

    [LoggerMessage(2, LogLevel.Warning, "Petición rechazada: Origin no autorizado ({Origin})")]
    public static partial void OriginRejected(ILogger logger, string origin);

    [LoggerMessage(3, LogLevel.Warning, "Intento de emparejamiento fallido desde {Ip}")]
    public static partial void PairingFailed(ILogger logger, string ip);

    [LoggerMessage(4, LogLevel.Information, "Dispositivo emparejado: {DeviceId}")]
    public static partial void Paired(ILogger logger, string deviceId);

    [LoggerMessage(5, LogLevel.Information, "Acción {Type} solicitada por {DeviceId}")]
    public static partial void ActionRequested(ILogger logger, string type, string deviceId);

    [LoggerMessage(6, LogLevel.Warning, "WebSocket rechazado: autenticación no válida")]
    public static partial void EventsAuthRejected(ILogger logger);

    [LoggerMessage(7, LogLevel.Information, "Canal de eventos abierto para {DeviceId}")]
    public static partial void EventsOpened(ILogger logger, string deviceId);

    [LoggerMessage(8, LogLevel.Information, "Canal de eventos cerrado para {DeviceId}")]
    public static partial void EventsClosed(ILogger logger, string deviceId);

    [LoggerMessage(9, LogLevel.Information, "Dispositivo {DeviceId} revocado: se cierra su canal")]
    public static partial void DeviceRevoked(ILogger logger, string deviceId);

    [LoggerMessage(10, LogLevel.Warning, "No se encuentra la PWA en {Root}; solo estará disponible la API")]
    public static partial void PwaMissing(ILogger logger, string root);

    [LoggerMessage(11, LogLevel.Error, "Fallo en el latido del canal de eventos (el servicio sigue)")]
    public static partial void HeartbeatFailed(ILogger logger, Exception exception);

    [LoggerMessage(12, LogLevel.Warning, "{Count} peticiones más rechazadas por Host/Origin en el último minuto")]
    public static partial void GuardRejectionsSuppressed(ILogger logger, int count);

    [LoggerMessage(13, LogLevel.Information, "Agente de sesión conectado (sesión {SessionId}, {Apps} apps autorizadas)")]
    public static partial void AgentConnected(ILogger logger, int sessionId, int apps);

    [LoggerMessage(14, LogLevel.Information, "Agente de sesión desconectado")]
    public static partial void AgentDisconnected(ILogger logger);

    [LoggerMessage(15, LogLevel.Warning, "Conexión al pipe rechazada: {Problem}")]
    public static partial void AgentRejected(ILogger logger, string problem);

    [LoggerMessage(16, LogLevel.Warning, "Fallo en el canal con el agente de sesión")]
    public static partial void AgentFailed(ILogger logger, Exception exception);

    [LoggerMessage(17, LogLevel.Warning, "apps.json: {Problem}")]
    public static partial void AppsFileProblem(ILogger logger, string problem);

    [LoggerMessage(18, LogLevel.Information, "El agente ya no está en la sesión activa de la consola: se desconecta")]
    public static partial void AgentLeftConsole(ILogger logger);

    [LoggerMessage(19, LogLevel.Information, "apps.json ha cambiado: {Count} apps autorizadas enviadas al agente")]
    public static partial void AppsReloaded(ILogger logger, int count);
}
