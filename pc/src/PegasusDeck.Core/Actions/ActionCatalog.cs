// Lista blanca de acciones que ejecuta el PC (espejo de pwa/src/core/actions.ts,
// solo las de ejecutor "service"). Cualquier otro tipo se rechaza.

using System.Text.RegularExpressions;
using PegasusDeck.Core.Contract;
using PegasusDeck.Core.Resolve;

namespace PegasusDeck.Core.Actions;

public enum ParamKind { AppId, ObsScene, ObsInput, Percent, ResolveCommand }

public sealed record ParamSpec(string Key, ParamKind Kind);

/// <summary>Dónde se ejecuta: hoy todo en el agente de sesión (el apagado del servicio se retiró, ADR-032).</summary>
public enum ActionTarget { Agent }

/// <summary>response: la respuesta es el resultado · event: se confirma después con un mensaje 'operation'.</summary>
public enum ActionCompletion { Response, Event }

/// <summary>Destructive: el servicio exige el segundo paso con confirmToken (ADR-024).</summary>
public sealed record ActionSpec(string Type, ActionTarget Target, IReadOnlyList<ParamSpec> Params, ActionCompletion Completion, int TimeoutMs, bool Destructive = false);

public sealed record ActionError(string Code, string Message);

public static partial class ActionCatalog
{
    private static readonly ActionSpec[] Specs =
    [
        new("windows.lock", ActionTarget.Agent, [], ActionCompletion.Response, 8000),
        new("app.open", ActionTarget.Agent, [new("appId", ParamKind.AppId)], ActionCompletion.Event, 30000),
        new("obs.recording.start", ActionTarget.Agent, [], ActionCompletion.Event, 10000),
        new("obs.recording.stop", ActionTarget.Agent, [], ActionCompletion.Event, 15000),
        new("obs.recording.pause", ActionTarget.Agent, [], ActionCompletion.Event, 10000),
        new("obs.streaming.start", ActionTarget.Agent, [], ActionCompletion.Event, 20000),
        new("obs.streaming.stop", ActionTarget.Agent, [], ActionCompletion.Event, 15000),
        new("obs.scene.set", ActionTarget.Agent, [new("scene", ParamKind.ObsScene)], ActionCompletion.Event, 5000),
        new("obs.input.toggleMute", ActionTarget.Agent, [new("input", ParamKind.ObsInput)], ActionCompletion.Event, 5000),
        new("audio.master.toggleMute", ActionTarget.Agent, [], ActionCompletion.Response, 5000),
        new("audio.master.setVolume", ActionTarget.Agent, [new("percent", ParamKind.Percent)], ActionCompletion.Response, 5000),
        new("audio.mic.toggleMute", ActionTarget.Agent, [], ActionCompletion.Response, 5000),
        new("media.play", ActionTarget.Agent, [], ActionCompletion.Response, 5000),
        new("media.pause", ActionTarget.Agent, [], ActionCompletion.Response, 5000),
        new("app.closeAll", ActionTarget.Agent, [new("appId", ParamKind.AppId)], ActionCompletion.Response, 10000, Destructive: true),
        new("davinci.command", ActionTarget.Agent, [new("command", ParamKind.ResolveCommand)], ActionCompletion.Response, 5000),
    ];

    private static readonly Dictionary<string, ActionSpec> ByType = Specs.ToDictionary(s => s.Type, StringComparer.Ordinal);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex IdRegex();

    public static IReadOnlyCollection<ActionSpec> All => Specs;

    public static ActionSpec? Find(string type) => ByType.GetValueOrDefault(type);

    /// <summary>Comprueba tipo y parámetros. Devuelve null si es válida.</summary>
    public static ActionError? Validate(ActionRequest request)
    {
        if (Find(request.Type) is not { } spec)
        {
            return new(ErrorCodes.UnknownAction, "Acción no registrada.");
        }
        foreach (var key in request.Params.Keys)
        {
            if (!spec.Params.Any(p => p.Key == key)) return new(ErrorCodes.BadParams, $"Parámetro no permitido: {key}");
        }
        foreach (var p in spec.Params)
        {
            if (!request.Params.TryGetValue(p.Key, out var value) || !IsValid(p.Kind, value))
            {
                return new(ErrorCodes.BadParams, $"Parámetro «{p.Key}» no válido.");
            }
        }
        return null;
    }

    private static bool IsValid(ParamKind kind, object value) => (kind, value) switch
    {
        (ParamKind.AppId, string s) => IdRegex().IsMatch(s),
        // Sin recortar: se usa el nombre exacto de OBS (puede tener espacios), pero no solo espacios.
        (ParamKind.ObsScene or ParamKind.ObsInput, string s) => s.Length <= 100 && s.Trim().Length > 0,
        (ParamKind.Percent, double d) => d >= 0 && d <= 100 && Math.Floor(d) == d,
        (ParamKind.ResolveCommand, string s) => ResolveCommands.IsKnown(s),
        _ => false,
    };
}
