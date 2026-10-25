using PegasusDeck.Core.Contract;
using Windows.Media.Control;

namespace PegasusDeck.Agent.Media;

/// <summary>Reproducción multimedia de la sesión del usuario (ADR-022).</summary>
internal interface IMediaController
{
    /// <summary>playing | paused | stopped | none | unavailable.</summary>
    MediaInfo Read();

    /// <summary>true si la app que reproduce acepta la orden (no significa que se haya cumplido).</summary>
    Task<bool> PlayAsync();

    Task<bool> PauseAsync();
}

/// <summary>Sin controles multimedia (pruebas o Windows sin SMTC).</summary>
internal sealed class NoMediaController : IMediaController
{
    public static NoMediaController Instance { get; } = new();

    public MediaInfo Read() => MediaInfo.Unavailable;

    public Task<bool> PlayAsync() => Task.FromResult(false);

    public Task<bool> PauseAsync() => Task.FromResult(false);
}

/// <summary>
/// Controles multimedia del sistema (System Media Transport Controls): la sesión
/// "actual" que Windows muestra en su control de volumen (Spotify, navegador,
/// reproductor…). Pausa y reanuda por separado, no como tecla de alternar.
/// </summary>
internal sealed class SmtcMediaController : IMediaController
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public MediaInfo Read()
    {
        try
        {
            var session = Manager().GetCurrentSession();
            if (session is null) return new MediaInfo("none");
            return session.GetPlaybackInfo()?.PlaybackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => new MediaInfo("playing"),
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => new MediaInfo("paused"),
                null => new MediaInfo("none"),
                _ => new MediaInfo("stopped"),
            };
        }
#pragma warning disable CA1031 // Cualquier fallo de WinRT = no disponible; el bucle de estado sigue.
        catch (Exception)
#pragma warning restore CA1031
        {
            _manager = null;
            return MediaInfo.Unavailable;
        }
    }

    public async Task<bool> PlayAsync() => Manager().GetCurrentSession() is { } s && await s.TryPlayAsync();

    public async Task<bool> PauseAsync() => Manager().GetCurrentSession() is { } s && await s.TryPauseAsync();

    private GlobalSystemMediaTransportControlsSessionManager Manager() =>
        _manager ??= GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult();
}
