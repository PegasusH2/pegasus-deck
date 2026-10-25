using System.Text.RegularExpressions;

namespace PegasusDeck.Core.Contract;

/// <summary>
/// Aplica a la instantánea los límites de snapshot.schema.json antes de enviarla.
/// La PWA rechaza mensajes fuera de contrato; sin esto, una escena de OBS con un
/// nombre de 300 caracteres dejaría la PWA mostrando datos viejos.
/// Lo que no se puede representar se omite (no se recorta: un nombre recortado no
/// identificaría la escena ni la fuente).
/// </summary>
public static partial class SnapshotLimits
{
    public const int MaxName = 100;
    public const int MaxList = 200;
    public const int MaxApps = 100;
    public const int MaxOutputs = 16;

    private static readonly HashSet<string> ObsStates = ["unavailable", "connecting", "ready", "auth_error", "incompatible"];
    private static readonly HashSet<string> AppStates = ["not_installed", "installed", "starting", "running", "ready"];
    private static readonly HashSet<string> MediaStates = ["playing", "paused", "stopped", "none", "unavailable"];

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex IdRegex();

    public static Snapshot Clamp(Snapshot s)
    {
        // Lo que llega deserializado puede traer nulos aunque los tipos digan lo contrario.
        var obs = s.Obs is { Scenes: not null, Inputs: not null, State: not null } ? s.Obs : ObsInfo.Unavailable;
        s = s with
        {
            Apps = s.Apps ?? [],
            Audio = s.Audio ?? AudioInfo.Unavailable,
            AppsDetection = s.AppsDetection ?? AppsDetection.Unavailable,
        };
        var obsState = ObsStates.Contains(obs.State) ? obs.State : "unavailable";
        var clampedObs = obs with
        {
            State = obsState,
            Scene = obs.Scene is { Length: <= MaxName } ? obs.Scene : null,
            Scenes = obs.Scenes.Where(n => n is { Length: <= MaxName }).Take(MaxList).ToList(),
            Inputs = obs.Inputs.Where(i => i?.Name is { Length: <= MaxName }).Take(MaxList).ToList(),
        };
        var apps = s.Apps
            .Where(a => a?.Id is not null && a.Name is not null && a.State is not null && IdRegex().IsMatch(a.Id) && AppStates.Contains(a.State))
            .Select(a => a with { Name = a.Name.Length > 60 ? a.Name[..60] : a.Name, Windows = a.Windows is { } w ? Math.Clamp(w, 0, 500) : null, Kind = a.Kind == "game" ? "game" : null })
            .Take(MaxApps)
            .ToList();
        var host = s.Windows.Hostname;
        var audio = s.Audio.State is "ready" or "unavailable"
            ? s.Audio with { Volume = Math.Clamp(s.Audio.Volume, 0, 100) }
            : AudioInfo.Unavailable;
        var mic = s.Mic is { State: "ready" or "unavailable" } ? s.Mic : MicInfo.Unavailable;
        var media = s.Media?.State is { } m && MediaStates.Contains(m) ? s.Media : MediaInfo.Unavailable;
        var outputs = s.Outputs is { State: "ready", Devices: not null }
            ? s.Outputs with
            {
                Devices = s.Outputs.Devices
                    .Where(d => d?.Id is not null && d.Name is not null && IdRegex().IsMatch(d.Id))
                    .Select(d => d with { Name = d.Name.Length > 60 ? d.Name[..60] : d.Name })
                    .Take(MaxOutputs)
                    .ToList(),
            }
            : OutputsInfo.Unavailable;
        return s with
        {
            Outputs = outputs,
            Mic = mic,
            Media = media,
            AppsDetection = s.AppsDetection is AppsDetection.Ok ? AppsDetection.Ok : AppsDetection.Unavailable,
            Audio = audio,
            Service = s.Service with { Version = s.Service.Version.Length > 40 ? s.Service.Version[..40] : s.Service.Version },
            Windows = s.Windows with { Hostname = host.Length > 63 ? host[..63] : host, UptimeSec = Math.Max(0, s.Windows.UptimeSec) },
            Obs = clampedObs,
            Apps = apps,
        };
    }
}
