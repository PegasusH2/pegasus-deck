using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using PegasusDeck.Agent.Apps;
using PegasusDeck.Agent.Audio;
using PegasusDeck.Agent.Media;
using PegasusDeck.Agent.Obs;
using PegasusDeck.Agent.Windows;
using PegasusDeck.Core.Actions;
using PegasusDeck.Core.Agent;
using PegasusDeck.Core.Apps;
using PegasusDeck.Core.Contract;
using PegasusDeck.Core.Native;
using PegasusDeck.Core.Resolve;
using PegasusDeck.Core.Security;

namespace PegasusDeck.Agent;

/// <summary>
/// Núcleo del agente: estado de la sesión (apps, audio, OBS) hacia el servicio y
/// ejecución de las órdenes que llegan, validadas de nuevo contra el catálogo y la
/// lista blanca recibida.
/// </summary>
internal sealed class AgentRuntime : IAgentHandler, IDisposable
{
    private readonly AgentOptions _options;
    private readonly IAudioController _audio;
    private readonly IMediaController _media;
    private readonly Channel<bool> _dirty = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly object _lock = new();
    private readonly List<(string OperationId, string AppId, DateTime Since)> _appWaiters = [];
    private IReadOnlyList<AppInfo> _apps = [];
    private bool _appsOk = true;
    private AudioInfo _audioState = AudioInfo.Unavailable;
    private MicInfo _micState = MicInfo.Unavailable;
    private MediaInfo _mediaState = MediaInfo.Unavailable;
    private OutputsInfo _outputsState = OutputsInfo.Unavailable;
    private CancellationToken _ct;

    public AgentRuntime(AgentOptions options, IAudioController audio, Func<int?>? obsProcessId = null, IMediaController? media = null)
    {
        _options = options;
        _audio = audio;
        _media = media ?? NoMediaController.Instance;
        Apps = new AppsMonitor();
        Connection = new ServiceConnection(options, this);
        Obs = new ObsClient(() => new ObsSettings(_options.ObsPort, _options.ObsPassword), obsProcessId ?? FindObsProcess);
        Obs.Changed += MarkDirty;
        Obs.OperationFinished += op => _ = Connection.SendAsync(op, _ct);
    }

    public AppsMonitor Apps { get; }

    public ServiceConnection Connection { get; }

    public ObsClient Obs { get; }

    /// <summary>Intervalo de detección de apps (más rápido mientras se espera a que una se abra).</summary>
    public TimeSpan AppsPoll { get; init; } = TimeSpan.FromSeconds(2);

    public Task RunAsync(CancellationToken ct)
    {
        _ct = ct;
        return Task.WhenAll(Connection.RunAsync(ct), Obs.RunAsync(ct), MonitorLoopAsync(ct), PublishLoopAsync(ct));
    }

    public AgentStateMessage CurrentState()
    {
        lock (_lock)
        {
            return new AgentStateMessage(true, Obs.State, _apps, _appsOk ? AppsDetection.Ok : AppsDetection.Unavailable, _audioState, _micState, _mediaState, _outputsState);
        }
    }

    // --- IAgentHandler ---------------------------------------------------------

    public void SetApps(IReadOnlyList<AppEntry> apps)
    {
        Apps.SetApps(apps);
        Refresh();
    }

    public Task OnConnectedAsync(CancellationToken ct) => Connection.SendAsync(CurrentState(), ct);

    public async Task<AgentResult> ExecuteAsync(ExecuteEnvelope request, CancellationToken ct)
    {
        var id = request.OperationId;
        var parameters = (request.Params ?? []).ToDictionary(kv => kv.Key, kv => ToValue(kv.Value));
        if (parameters.Values.Any(v => v is null)) return AgentResult.Failed(id, ErrorCodes.BadParams, "Parámetros no válidos.");
        var typed = parameters.ToDictionary(kv => kv.Key, kv => kv.Value!);
        // Defensa en profundidad: el agente no confía ciegamente en lo que llega por el pipe.
        if (ActionCatalog.Validate(new ActionRequest(request.Action, typed)) is { } error) return AgentResult.Failed(id, error.Code, error.Message);

        try
        {
            switch (request.Action)
            {
                case "windows.lock":
                    return NativeMethods.LockWorkStation()
                        ? AgentResult.Completed(id, "Sesión bloqueada.")
                        : AgentResult.Failed(id, ErrorCodes.Internal, "Windows no ha permitido bloquear la sesión.");
                case "audio.master.toggleMute":
                {
                    var s = _audio.Read();
                    if (s.State != "ready") return AgentResult.Failed(id, ErrorCodes.NotFound, "No hay ninguna salida de audio activa.");
                    _audio.SetMute(!s.Muted);
                    Refresh();
                    return AgentResult.Completed(id, s.Muted ? "Audio activado." : "Audio silenciado.");
                }
                case "audio.master.setVolume":
                {
                    if (_audio.Read().State != "ready") return AgentResult.Failed(id, ErrorCodes.NotFound, "No hay ninguna salida de audio activa.");
                    var percent = (int)(double)typed["percent"];
                    _audio.SetVolume(percent);
                    Refresh();
                    return AgentResult.Completed(id, $"Volumen: {percent} %.");
                }
                case "audio.mic.toggleMute":
                {
                    var mic = _audio.ReadMic();
                    if (mic.State != "ready") return AgentResult.Failed(id, ErrorCodes.NotFound, "No hay ningún micrófono activo.");
                    _audio.SetMicMute(!mic.Muted);
                    var now = _audio.ReadMic();
                    Refresh();
                    // Se confirma leyendo el estado real, no por haber enviado la orden.
                    if (now.State != "ready" || now.Muted == mic.Muted) return AgentResult.Failed(id, ErrorCodes.Internal, "Windows no ha cambiado el micrófono.");
                    return AgentResult.Completed(id, now.Muted ? "Micrófono silenciado." : "Micrófono activado.");
                }
                case "audio.output.set":
                {
                    var device = (string)typed["device"];
                    var target = _audio.ReadOutputs().Devices.FirstOrDefault(d => d.Id == device);
                    if (target is null) return AgentResult.Failed(id, ErrorCodes.NotFound, "Esa salida de audio ya no está conectada.");
                    if (target.Default)
                    {
                        Refresh();
                        return AgentResult.Completed(id, $"Ya suena por {target.Name}.");
                    }
                    var problem = _audio.SetDefaultOutput(device);
                    Refresh();
                    return problem is null
                        ? AgentResult.Completed(id, $"Ahora suena por {target.Name}.")
                        : AgentResult.Failed(id, ErrorCodes.Internal, problem);
                }
                case "media.pause":
                    return await MediaAsync(id, ["playing"], "paused", "pausar", "Reproducción en pausa.", _media.PauseAsync, ct);
                case "media.play":
                    return await MediaAsync(id, ["paused", "stopped"], "playing", "reanudar", "Reproducción reanudada.", _media.PlayAsync, ct);
                case "app.open":
                    return OpenApp(id, (string)typed["appId"]);
                case "davinci.command":
                    return SendToResolve(id, (string)typed["command"]);
                case "app.closeAll":
                    return await CloseAllAsync(id, (string)typed["appId"], ct);
                default:
                    return await Obs.ExecuteAsync(id, request.Action, typed, ct);
            }
        }
#pragma warning disable CA1031 // Cualquier fallo se devuelve como resultado; el agente sigue funcionando.
        catch (Exception e)
#pragma warning restore CA1031
        {
            return AgentResult.Failed(id, ErrorCodes.Internal, LineChannel.Printable(e.Message));
        }
    }

    /// <summary>
    /// Pausa o reanuda la sesión multimedia actual de Windows y lo confirma leyendo su
    /// estado (una app puede aceptar la orden y no cumplirla).
    /// </summary>
    private async Task<AgentResult> MediaAsync(
        string id, string[] from, string to, string verb, string done, Func<Task<bool>> command, CancellationToken ct)
    {
        var before = _media.Read();
        if (before.State == "unavailable") return AgentResult.Failed(id, ErrorCodes.NotFound, "Los controles multimedia de Windows no están disponibles.");
        if (before.State == "none") return AgentResult.Failed(id, ErrorCodes.Conflict, "No hay nada reproduciéndose en el PC.");
        if (!from.Contains(before.State)) return AgentResult.Failed(id, ErrorCodes.Conflict, $"No se puede {verb}: la reproducción no está en ese estado.");
        if (!await command()) return AgentResult.Failed(id, ErrorCodes.Conflict, $"La app que reproduce no permite {verb}.");
        for (var i = 0; i < 20; i++)
        {
            if (_media.Read().State == to)
            {
                Refresh();
                return AgentResult.Completed(id, done);
            }
            await Task.Delay(100, ct);
        }
        Refresh();
        return AgentResult.Failed(id, ErrorCodes.Timeout, $"La app aceptó la orden pero no ha llegado a {verb}.");
    }

    /// <summary>
    /// Cierra todas las ventanas de una app autorizada como si se pulsara su X (WM_CLOSE): nunca se
    /// fuerza ni se termina el proceso; una app con cambios sin guardar puede preguntar en el PC.
    /// El resultado dice cuántas se han cerrado de verdad.
    /// </summary>
    private async Task<AgentResult> CloseAllAsync(string id, string appId, CancellationToken ct)
    {
        var app = Apps.Find(appId);
        if (app is null) return AgentResult.Failed(id, ErrorCodes.AppNotAllowed, "Aplicación no autorizada en este PC.");
        var windows = Apps.ClosableWindows(app);
        if (windows.Count == 0) return AgentResult.Failed(id, ErrorCodes.Conflict, $"{app.Name} no tiene ventanas abiertas.");
        foreach (var hwnd in windows) NativeMethods.PostMessage(hwnd, NativeMethods.WmClose, IntPtr.Zero, IntPtr.Zero);

        var remaining = windows.Count;
        for (var i = 0; i < 30 && remaining > 0; i++)
        {
            await Task.Delay(100, ct);
            remaining = Apps.ClosableWindows(app).Count(windows.Contains);
        }
        Refresh();
        var closed = windows.Count - remaining;
        var noun = windows.Count == 1 ? "ventana" : "ventanas";
        return remaining == 0
            ? AgentResult.Completed(id, $"Cerrada{(closed == 1 ? "" : "s")} {closed} {noun} de {app.Name}.")
            : AgentResult.Completed(id, $"Se han cerrado {closed} de {windows.Count} {noun} de {app.Name}; {remaining} siguen abiertas (puede que pidan guardar cambios en el PC).");
    }

    /// <summary>
    /// Pulsa en DaVinci Resolve la combinación de una orden de la lista cerrada (ADR-028). Solo si Resolve
    /// está autorizado y abierto, se ha podido traer al frente y la ventana activa es suya justo antes de
    /// pulsar: nunca se envían teclas a otra app.
    /// </summary>
    private AgentResult SendToResolve(string id, string command)
    {
        if (!ResolveCommands.All.TryGetValue(command, out var spec)) return AgentResult.Failed(id, ErrorCodes.BadParams, "Orden de DaVinci Resolve desconocida.");
        // El atajo que tiene el usuario en SU Resolve (preset activo, ADR-031); si no se puede leer, el por defecto.
        var (key, source) = ResolveKeymap.Resolve(ResolveKeymap.Load(), spec);
        if (key is null) return AgentResult.Failed(id, ErrorCodes.Conflict, $"Sin atajo en Resolve ({source}): asígnalo en Resolve con Ctrl+Alt+K.");
        var app = Apps.Apps.FirstOrDefault(a => Path.GetFileName(a.ExePath).Equals("Resolve.exe", StringComparison.OrdinalIgnoreCase));
        if (app is null) return AgentResult.Failed(id, ErrorCodes.AppNotAllowed, "DaVinci Resolve no está autorizada en este PC.");
        if (Apps.StateOf(app) is not ("ready" or "running")) return AgentResult.Failed(id, ErrorCodes.Conflict, "DaVinci Resolve no está abierto.");
        var hwnd = Apps.MainWindow(app);
        if (hwnd == IntPtr.Zero) return AgentResult.Failed(id, ErrorCodes.Conflict, "DaVinci Resolve no tiene ninguna ventana visible.");
        AppLauncher.BringToFront(hwnd);
        var resolveThread = NativeMethods.GetWindowThreadProcessId(hwnd, out var resolvePid);
        NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out var foregroundPid);
        if (foregroundPid != resolvePid)
        {
            return AgentResult.Failed(id, ErrorCodes.Conflict, "Windows no ha dejado poner DaVinci Resolve delante: haz clic en Resolve en el PC y vuelve a intentarlo.");
        }
        return Keyboard.Send(key.Value, NativeMethods.GetKeyboardLayout(resolveThread)) is { } problem
            ? AgentResult.Failed(id, ErrorCodes.Internal, $"No se ha podido pulsar {ResolveCommands.Describe(key.Value)}: {problem}.")
            : AgentResult.Completed(id, $"Enviado a DaVinci Resolve ({ResolveCommands.Describe(key.Value)}, {source}).");
    }

    private AgentResult OpenApp(string id, string appId)
    {
        var app = Apps.Find(appId);
        if (app is null) return AgentResult.Failed(id, ErrorCodes.AppNotAllowed, "Aplicación no autorizada en este PC.");
        if (app.Aumid is null && !File.Exists(app.ExePath)) return AgentResult.Failed(id, ErrorCodes.AppNotFound, $"{app.Name} no está instalada en la ruta autorizada.");
        if (app.Aumid is not null && Apps.StateOf(app) == "not_installed") return AgentResult.Failed(id, ErrorCodes.AppNotFound, $"{app.Name} ya no está instalada para este usuario.");
        // Las apps de Store las protege Windows (paquete firmado en una carpeta del sistema).
        if (app.Aumid is null && !app.AllowUnprotectedPath && ExecutableSecurity.Problem(app.ExePath) is { } risk)
        {
            return AgentResult.Failed(id, ErrorCodes.AppNotAllowed,
                $"No se abre {app.Name}: {risk}. Un administrador debe instalarla en una carpeta protegida o permitirlo expresamente en apps.json.");
        }

        if (app.AlwaysLaunch)
        {
            return AppLauncher.Launch(app) is { } why
                ? AgentResult.Failed(id, ErrorCodes.AppNotAllowed, $"No se ha abierto {app.Name}: {why}.")
                : AgentResult.Completed(id, $"Se ha pedido abrir una ventana nueva de {app.Name}.");
        }
        var state = Apps.StateOf(app);
        if (state == "starting")
        {
            // Ya se está abriendo: no se lanza otra vez; se espera a que esté lista.
            lock (_lock) _appWaiters.Add((id, app.Id, DateTime.UtcNow));
            return AgentResult.Accepted(id, $"{app.Name} se está abriendo…");
        }
        if (state is "ready" or "running")
        {
            var hwnd = Apps.MainWindow(app);
            if (hwnd == IntPtr.Zero) return AgentResult.Completed(id, $"{app.Name} ya está abierta, sin ventana visible (puede estar en la bandeja del sistema).");
            return AppLauncher.BringToFront(hwnd)
                ? AgentResult.Completed(id, $"{app.Name} traída al frente.")
                : AgentResult.Completed(id, $"{app.Name} ya estaba abierta (Windows no ha permitido traerla al frente mientras se usa el PC).");
        }
        if (AppLauncher.Launch(app) is { } reason) return AgentResult.Failed(id, ErrorCodes.AppNotAllowed, $"No se ha abierto {app.Name}: {reason}.");
        Apps.MarkLaunched(app.Id);
        lock (_lock) _appWaiters.Add((id, app.Id, DateTime.UtcNow));
        Refresh();
        return AgentResult.Accepted(id, $"Abriendo {app.Name}…");
    }

    // --- Bucles ----------------------------------------------------------------

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Refresh();
            }
#pragma warning disable CA1031 // Un fallo puntual no debe parar la detección para siempre.
            catch (Exception)
#pragma warning restore CA1031
            {
                lock (_lock) _appsOk = false;
                MarkDirty();
            }
            bool waiting;
            lock (_lock) waiting = _appWaiters.Count > 0;
            await Task.Delay(waiting ? TimeSpan.FromMilliseconds(500) : AppsPoll, ct);
        }
    }

    /// <summary>Recalcula apps y audio; confirma las aperturas pendientes; marca cambios.</summary>
    private void Refresh()
    {
        var ok = Apps.TrySnapshot(out var apps);
        var audio = _audio.Read();
        var mic = _audio.ReadMic();
        var media = _media.Read();
        var outputs = _audio.ReadOutputs();
        List<AgentOperation> finished = [];
        bool changed;
        lock (_lock)
        {
            changed = ok != _appsOk || !apps.SequenceEqual(_apps) || audio != _audioState || mic != _micState || media != _mediaState || !OutputsInfo.Same(outputs, _outputsState);
            _appsOk = ok;
            _apps = apps;
            _audioState = audio;
            _micState = mic;
            _mediaState = media;
            _outputsState = outputs;
            foreach (var w in _appWaiters.ToList())
            {
                var info = apps.FirstOrDefault(a => a.Id == w.AppId);
                if (info?.State == "ready")
                {
                    _appWaiters.Remove(w);
                    finished.Add(new AgentOperation(w.OperationId, "completed", null, $"{info.Name} lista."));
                }
                else if (DateTime.UtcNow - w.Since > AppsMonitor.StartingWindow + TimeSpan.FromSeconds(5))
                {
                    _appWaiters.Remove(w); // el servicio ya habrá dado la operación por agotada
                }
            }
        }
        foreach (var op in finished) _ = Connection.SendAsync(op, _ct);
        if (changed) MarkDirty();
    }

    private void MarkDirty() => _dirty.Writer.TryWrite(true);

    /// <summary>Envía el estado al cambiar (agrupado 150 ms) y como latido cada 10 s.</summary>
    private async Task PublishLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(ct);
            heartbeat.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await _dirty.Reader.ReadAsync(heartbeat.Token);
                await Task.Delay(150, ct);
                _dirty.Reader.TryRead(out _);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // latido
            }
            try
            {
                await Connection.SendAsync(CurrentState(), ct);
            }
            catch (IOException)
            {
                // sin conexión: se enviará al reconectar
            }
        }
    }

    private static object? ToValue(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    public void Dispose() => Obs.Dispose();

    /// <summary>
    /// OBS de ESTA sesión y de la ruta esperada (la de la lista blanca o la instalación estándar):
    /// un "obs64.exe" de otro usuario no cuenta.
    /// </summary>
    private int? FindObsProcess()
    {
        var expected = Apps.Find("obs")?.ExePath
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"obs-studio\bin\64bit\obs64.exe");
        using var self = Process.GetCurrentProcess();
        var obs = Process.GetProcessesByName("obs64");
        try
        {
            return obs.FirstOrDefault(p => p.SessionId == self.SessionId && ProcessIdentity.SamePath(ProcessIdentity.ImagePath(p.Id), expected))?.Id;
        }
        finally
        {
            foreach (var p in obs) p.Dispose();
        }
    }
}
