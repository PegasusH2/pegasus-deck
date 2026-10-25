using System.Reflection;
using PegasusDeck.Core.Actions;
using PegasusDeck.Core.Contract;
using PegasusDeck.Core.Native;
using PegasusDeck.Core.Security;
using PegasusDeck.Service.Agent;

namespace PegasusDeck.Service;

/// <summary>Resultado de ejecutar una acción.</summary>
public abstract record ActionOutcome
{
    public sealed record Completed(string Message) : ActionOutcome;
    public sealed record Accepted(string OperationId, string? Message) : ActionOutcome;
    public sealed record Failed(string Code, string Message) : ActionOutcome;
    public sealed record ConfirmRequired(string Token, int ExpiresInSec, string Message) : ActionOutcome;
}

/// <summary>Estado que informa el agente de sesión (sesión, OBS, apps, audio).</summary>
public sealed record AgentState(
    bool SessionReady, ObsInfo Obs, IReadOnlyList<AppInfo> Apps, string AppsDetection, AudioInfo Audio, MicInfo? Mic = null, MediaInfo? Media = null,
    OutputsInfo? Outputs = null)
{
    public static AgentState None { get; } = new(false, ObsInfo.Unavailable, [], Core.Contract.AppsDetection.Unavailable, AudioInfo.Unavailable);
}

/// <summary>
/// Canal con el agente de la sesión del usuario (named pipe, ADR-016). Todo lo que
/// toca el escritorio (abrir apps, bloquear, audio, OBS) pasa por aquí. Los cambios
/// de estado y las confirmaciones llegan por ServiceEvents / OperationTracker (P-2).
/// </summary>
public interface IAgentLink
{
    AgentState Current { get; }

    /// <summary>El operationId lo genera el servicio (dueño del seguimiento y del tiempo máximo).</summary>
    Task<ActionOutcome> ExecuteAsync(ActionRequest request, string operationId, CancellationToken ct);
}

public sealed class SystemState(IAgentLink agent)
{
    public ServiceInfo Service { get; } = new(Protocol.ServiceName, ReadVersion());

    public Snapshot Build()
    {
        var a = agent.Current;
        // Siempre dentro de los límites del contrato (lo que no cabe se omite).
        return SnapshotLimits.Clamp(new Snapshot(
            Protocol.Version,
            Service,
            new WindowsInfo(true, Environment.MachineName, Environment.TickCount64 / 1000),
            new SessionInfo(a.SessionReady),
            a.SessionReady ? a.Obs : ObsInfo.Unavailable,
            a.SessionReady ? a.Apps : [],
            a.SessionReady ? a.AppsDetection : AppsDetection.Unavailable,
            a.SessionReady ? a.Audio : AudioInfo.Unavailable,
            a.SessionReady ? a.Mic : MicInfo.Unavailable,
            a.SessionReady ? a.Media : MediaInfo.Unavailable,
            a.SessionReady ? a.Outputs : OutputsInfo.Unavailable));
    }

    private static string ReadVersion()
    {
        var v = typeof(SystemState).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = v.IndexOf('+', StringComparison.Ordinal);
        v = plus > 0 ? v[..plus] : v;
        return v.Length > 40 ? v[..40] : v;
    }
}

/// <summary>Valida contra la lista blanca, envía la acción y sigue su confirmación.</summary>
public sealed class ActionDispatcher(
    IAgentLink agent, OperationTracker tracker, Confirmations confirmations, ILogger<ActionDispatcher> log)
{
    /// <summary>Margen sobre el tiempo de la acción: la PWA agota antes su espera y avisa ella.</summary>
    private static readonly TimeSpan ServerGrace = TimeSpan.FromSeconds(2);


    public async Task<ActionOutcome> DispatchAsync(ActionRequest request, string deviceId, CancellationToken ct)
    {
        if (ActionCatalog.Validate(request) is { } error) return new ActionOutcome.Failed(error.Code, error.Message);
        var spec = ActionCatalog.Find(request.Type)!;
        Log.ActionRequested(log, request.Type, deviceId);

        if (spec.Destructive)
        {
            // Primer paso: no se ejecuta nada. Segundo paso: el token de un solo uso de este dispositivo y esta acción.
            if (request.ConfirmToken is null)
            {
                return new ActionOutcome.ConfirmRequired(confirmations.Issue(deviceId, request), (int)Confirmations.Lifetime.TotalSeconds, "Confirma la acción.");
            }
            if (!confirmations.Consume(request.ConfirmToken, deviceId, request))
            {
                return new ActionOutcome.Failed(ErrorCodes.ConfirmationInvalid, "La confirmación ha caducado o no es válida. Vuelve a intentarlo.");
            }
        }

        // Se registra ANTES de enviarla: la confirmación puede llegar justo detrás de la respuesta.
        var operationId = Tokens.NewOperationId();
        tracker.Track(operationId, TimeSpan.FromMilliseconds(spec.TimeoutMs) + ServerGrace);
        var outcome = await agent.ExecuteAsync(request, operationId, ct);
        if (outcome is not ActionOutcome.Accepted) tracker.Forget(operationId);
        return outcome;
    }
}
