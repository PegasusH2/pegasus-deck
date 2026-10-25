using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PegasusDeck.Core.Agent;
using PegasusDeck.Core.Contract;
using PegasusDeck.Core.Native;

namespace PegasusDeck.Agent.Obs;

internal sealed record ObsSettings(int Port, string? Password);

/// <summary>
/// Cliente obs-websocket 5 (skill pegasus-obs). Reglas:
///  - Petición aceptada ≠ acción completada: solo el evento de OBS (o comprobar su estado) confirma.
///  - Algunos fallos de OBS no generan evento (cuadro de diálogo en el PC): se comprueba el estado al poco.
///  - Contraseña incorrecta o versión incompatible: no se reintenta en bucle.
///  - Al reconectar o cambiar de colección de escenas se relee el estado completo.
///  - Solo se habla con el proceso de OBS que escucha en el puerto (no se envía la prueba de la contraseña a otro).
/// </summary>
internal sealed class ObsClient(Func<ObsSettings> settings, Func<int?> obsProcessId, Func<int, int?>? portOwner = null) : IDisposable
{
    // Suscripciones: General (1) | Config (2) | Scenes (4) | Inputs (8) | Outputs (64).
    private const int EventSubscriptions = 1 | 2 | 4 | 8 | 64;
    /// <summary>Menor que la espera del servicio (8 s): si OBS tarda, se sabe aquí antes de que el servicio desista.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);
    /// <summary>Tiempo tras el que se comprueba el estado si OBS no ha enviado el evento (fallos sin evento).</summary>
    private static readonly TimeSpan VerifyAfter = TimeSpan.FromSeconds(2);
    /// <summary>Las esperas de confirmación caducan en el agente (el servicio ya habrá dado la operación por agotada).</summary>
    private static readonly TimeSpan WaiterLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15)];

    private sealed record Waiter(string OperationId, Func<string, JsonElement, (bool Ok, string Message)?> Match, DateTime Since);

    private readonly Func<int, int?> _portOwner = portOwner ?? TcpListeners.OwnerOf;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _requests = new(StringComparer.Ordinal);
    private readonly List<Waiter> _waiters = [];
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly SemaphoreSlim _reload = new(1, 1);
    private ClientWebSocket? _ws;
    private ObsInfo _state = ObsInfo.Unavailable;
    private (int Pid, string? Password)? _blocked;
    private volatile bool _collectionChanging;
    private int _eventsSeen;

    /// <summary>Tiempo entre comprobaciones cuando OBS no está abierto (pruebas: más corto).</summary>
    public TimeSpan IdlePoll { get; init; } = TimeSpan.FromSeconds(2);

    public event Action? Changed;

    public event Action<AgentOperation>? OperationFinished;

    public ObsInfo State
    {
        get { lock (_lock) return _state; }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            var pid = obsProcessId();
            if (pid is null)
            {
                _blocked = null;
                attempt = 0;
                SetState(ObsInfo.Unavailable);
                await Task.Delay(IdlePoll, ct);
                continue;
            }
            var s = settings();
            if (_blocked is { } b && b.Pid == pid && b.Password == s.Password)
            {
                await Task.Delay(IdlePoll, ct); // se mantiene auth_error / incompatible
                continue;
            }
            // Solo se habla con el proceso de OBS que escucha en el puerto.
            if (_portOwner(s.Port) != pid)
            {
                SetState(ObsInfo.Unavailable with { State = "connecting" });
                await Task.Delay(Backoff[Math.Min(attempt++, Backoff.Length - 1)], ct);
                continue;
            }
            try
            {
                SetState(ObsInfo.Unavailable with { State = "connecting" });
                await SessionAsync(s, ct);
                attempt = 0;
            }
            catch (ObsRejectedException e)
            {
                _blocked = (pid.Value, s.Password);
                SetState(ObsInfo.Unavailable with { State = e.State });
                continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e) when (e is WebSocketException or IOException or TimeoutException or OperationCanceledException or JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
            {
                // OBS aún arrancando, servidor WebSocket desactivado o conexión caída: se reintenta.
            }
            FailWaiters("OBS se ha desconectado antes de confirmar la acción.");
            if (obsProcessId() is not null)
            {
                SetState(ObsInfo.Unavailable with { State = "connecting" });
                await Task.Delay(Backoff[Math.Min(attempt++, Backoff.Length - 1)], ct);
            }
        }
    }

    public async Task<AgentResult> ExecuteAsync(string operationId, string action, IReadOnlyDictionary<string, object> p, CancellationToken ct)
    {
        var state = State;
        if (state.State != "ready") return AgentResult.Failed(operationId, ErrorCodes.ObsNotReady, "OBS no está conectado.");
        if (_collectionChanging) return AgentResult.Failed(operationId, ErrorCodes.Busy, "OBS está cambiando de colección de escenas; inténtalo en un momento.");
        string Str(string key) => (string)p[key];

        string? conflict = action switch
        {
            "obs.recording.start" when state.Recording => "La grabación ya está en curso.",
            "obs.recording.stop" or "obs.recording.pause" when !state.Recording => "No hay ninguna grabación en curso.",
            "obs.streaming.start" when state.Streaming => "El streaming ya está activo.",
            "obs.streaming.stop" when !state.Streaming => "No hay ninguna emisión activa.",
            _ => null,
        };
        if (conflict is not null) return AgentResult.Failed(operationId, ErrorCodes.Conflict, conflict);
        if (action == "obs.scene.set" && state.Scene == Str("scene")) return AgentResult.Completed(operationId, "Ya era la escena activa.");
        if (action == "obs.scene.set" && !state.Scenes.Contains(Str("scene")))
        {
            return AgentResult.Failed(operationId, ErrorCodes.NotFound, $"La escena «{Str("scene")}» no existe en OBS.");
        }
        if (action == "obs.input.toggleMute" && !state.Inputs.Any(i => i.Name == Str("input")))
        {
            return AgentResult.Failed(operationId, ErrorCodes.NotFound, $"La fuente «{Str("input")}» no existe en OBS.");
        }

        var resume = state.RecordingPaused;
        var (request, data, waiter, acceptedMessage, verify) = action switch
        {
            "obs.recording.start" => ("StartRecord", (JsonObject?)null,
                OutputWaiter(operationId, "RecordStateChanged", "STARTED", "Grabación iniciada.", "OBS no pudo iniciar la grabación."),
                "Iniciando grabación…",
                // OBS 29: carpeta no válida, poco espacio o fallo al arrancar el codificador → solo un aviso en el PC.
                (Func<JsonElement, (bool, string)?>?)(s => Bool(s, "outputActive")
                    ? (true, "Grabación iniciada.")
                    : (false, "OBS no ha podido iniciar la grabación (revisa el aviso en OBS: carpeta, espacio libre o codificador)."))),
            "obs.recording.stop" => ("StopRecord", null, OutputWaiter(operationId, "RecordStateChanged", "STOPPED", "Grabación detenida.", null), "Deteniendo grabación…", null),
            "obs.recording.pause" when resume => ("ResumeRecord", null, StateWaiter(operationId, "RESUMED", "Grabación reanudada."), "Reanudando grabación…",
                s => !Bool(s, "outputPaused") ? (true, "Grabación reanudada.") : (false, "OBS no ha reanudado la grabación.")),
            "obs.recording.pause" => ("PauseRecord", null, StateWaiter(operationId, "PAUSED", "Grabación en pausa."), "Pausando grabación…",
                // OBS 29 no pausa si la grabación comparte codificador con la emisión: ni error ni evento.
                s => Bool(s, "outputPaused") ? (true, "Grabación en pausa.") : (false, "OBS no permite pausar la grabación con el codificador configurado (usa uno propio para la grabación).")),
            "obs.streaming.start" => ("StartStream", null,
                OutputWaiter(operationId, "StreamStateChanged", "STARTED", "Streaming iniciado.", "OBS no pudo iniciar la emisión (revisa el servicio de streaming en OBS)."),
                "Iniciando streaming…", null),
            "obs.streaming.stop" => ("StopStream", null, OutputWaiter(operationId, "StreamStateChanged", "STOPPED", "Streaming detenido.", null), "Deteniendo streaming…", null),
            "obs.scene.set" => ("SetCurrentProgramScene", new JsonObject { ["sceneName"] = Str("scene") },
                new Waiter(operationId, (type, d) => type == "CurrentProgramSceneChanged" && Prop(d, "sceneName") == Str("scene") ? (true, $"Escena: {Str("scene")}.") : null, DateTime.UtcNow),
                "Cambiando de escena…", null),
            "obs.input.toggleMute" => ("ToggleInputMute", new JsonObject { ["inputName"] = Str("input") },
                new Waiter(operationId, (type, d) => type == "InputMuteStateChanged" && Prop(d, "inputName") == Str("input")
                    ? (true, Bool(d, "inputMuted") ? $"{Str("input")} silenciado." : $"{Str("input")} activado.")
                    : null, DateTime.UtcNow),
                "Cambiando la fuente…", null),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        // El evento puede llegar antes que la respuesta: se espera desde ANTES de enviar.
        lock (_lock) _waiters.Add(waiter);
        bool ok;
        int code;
        string? comment;
        try
        {
            (ok, code, comment, _) = await RequestAsync(request, data, ct);
        }
        catch
        {
            lock (_lock) _waiters.Remove(waiter);
            throw;
        }
        if (!ok)
        {
            lock (_lock) _waiters.Remove(waiter);
            return AgentResult.Failed(operationId, MapCode(code), comment ?? "OBS ha rechazado la petición.");
        }
        if (verify is not null) _ = VerifyLaterAsync(waiter, verify, ct);
        return AgentResult.Accepted(operationId, acceptedMessage);
    }

    /// <summary>Si el evento no llega, se consulta el estado de la grabación y se cierra la operación con un motivo concreto.</summary>
    private async Task VerifyLaterAsync(Waiter waiter, Func<JsonElement, (bool Ok, string Message)?> check, CancellationToken ct)
    {
        try
        {
            await Task.Delay(VerifyAfter, ct);
            bool pending;
            lock (_lock) pending = _waiters.Contains(waiter);
            if (!pending) return;
            var status = await RequestAsync("GetRecordStatus", null, ct);
            if (!status.Ok || check(status.Data) is not { } r) return;
            lock (_lock)
            {
                if (!_waiters.Remove(waiter)) return; // llegó el evento mientras tanto
            }
            OperationFinished?.Invoke(new AgentOperation(waiter.OperationId, r.Ok ? "completed" : "failed", r.Ok ? null : ErrorCodes.Conflict, r.Message));
            if (r.Ok) _ = ReloadAsync(ct);
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or KeyNotFoundException)
        {
            // Sin conexión: FailWaiters ya cierra la operación.
        }
    }

    // --- Sesión con OBS --------------------------------------------------------

    private async Task SessionAsync(ObsSettings s, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol("obswebsocket.json");
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(ct);
        handshake.CancelAfter(HandshakeTimeout);
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{s.Port}"), handshake.Token);

        using (var hello = await ReceiveAsync(ws, handshake.Token) ?? throw ClosedDuringHandshake(ws))
        {
            if (Op(hello) != 0) throw new InvalidDataException("Se esperaba Hello de OBS.");
            var d = hello.RootElement.GetProperty("d");
            var identify = new JsonObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = EventSubscriptions };
            if (d.TryGetProperty("authentication", out var auth))
            {
                if (string.IsNullOrEmpty(s.Password)) throw new ObsRejectedException("auth_error");
                identify["authentication"] = AuthString(s.Password, auth.GetProperty("salt").GetString()!, auth.GetProperty("challenge").GetString()!);
            }
            await SendAsync(ws, new JsonObject { ["op"] = 1, ["d"] = identify }, handshake.Token);
        }
        using (var identified = await ReceiveAsync(ws, handshake.Token) ?? throw ClosedDuringHandshake(ws))
        {
            if (Op(identified) != 2) throw new InvalidDataException("Se esperaba Identified de OBS.");
        }

        _ws = ws;
        _collectionChanging = false;
        var reader = ReadLoopAsync(ws, ct);
        try
        {
            await ReloadAsync(ct);
            await reader;
        }
        finally
        {
            _ws = null;
            foreach (var (_, tcs) in _requests) tcs.TrySetException(new IOException("OBS se ha desconectado."));
            _requests.Clear();
        }
    }

    private static Exception ClosedDuringHandshake(ClientWebSocket ws) => (int?)ws.CloseStatus switch
    {
        4009 => new ObsRejectedException("auth_error"),     // AuthenticationFailed
        4010 => new ObsRejectedException("incompatible"),   // UnsupportedRpcVersion
        _ => new IOException("OBS cerró la conexión durante el saludo."),
    };

    /// <summary>Relee todo el estado. Una sola recarga a la vez; si llegan eventos durante la carga, se repite.</summary>
    private async Task ReloadAsync(CancellationToken ct)
    {
        if (!await _reload.WaitAsync(0, ct)) return; // ya hay una en curso
        try
        {
            for (var round = 0; round < 3; round++)
            {
                var seen = Volatile.Read(ref _eventsSeen);
                var next = await ReadFullStateAsync(ct);
                SetState(next);
                if (Volatile.Read(ref _eventsSeen) == seen) return; // nada ha cambiado mientras se leía
            }
        }
        catch (Exception e) when (e is IOException or TimeoutException or KeyNotFoundException or InvalidOperationException)
        {
            // La conexión se ha cortado: el bucle principal reconecta.
        }
        finally
        {
            _reload.Release();
        }
    }

    private async Task<ObsInfo> ReadFullStateAsync(CancellationToken ct)
    {
        var scenes = await RequestAsync("GetSceneList", null, ct);
        var record = await RequestAsync("GetRecordStatus", null, ct);
        var stream = await RequestAsync("GetStreamStatus", null, ct);
        var inputs = await ReadAudioInputsAsync(ct);
        var sceneNames = scenes.Ok && scenes.Data.TryGetProperty("scenes", out var list)
            // OBS las devuelve de abajo arriba (Obs_ArrayHelper: "Reverse the vector order").
            ? list.EnumerateArray().Select(x => Prop(x, "sceneName")).OfType<string>().Reverse().ToList()
            : [];
        return new ObsInfo(
            "ready",
            record.Ok && Bool(record.Data, "outputActive"), // en pausa sigue siendo true en GetRecordStatus
            record.Ok && Bool(record.Data, "outputPaused"),
            stream.Ok && Bool(stream.Data, "outputActive"),
            scenes.Ok ? Prop(scenes.Data, "currentProgramSceneName") : null,
            sceneNames,
            inputs);
    }

    /// <summary>Fuentes con audio: las que responden a GetInputMute.</summary>
    private async Task<List<ObsInput>> ReadAudioInputsAsync(CancellationToken ct)
    {
        var list = await RequestAsync("GetInputList", null, ct);
        var result = new List<ObsInput>();
        if (!list.Ok || !list.Data.TryGetProperty("inputs", out var inputs)) return result;
        foreach (var name in inputs.EnumerateArray().Select(i => Prop(i, "inputName")).OfType<string>().Take(SnapshotLimits.MaxList))
        {
            var mute = await RequestAsync("GetInputMute", new JsonObject { ["inputName"] = name }, ct);
            if (mute.Ok) result.Add(new ObsInput(name, Bool(mute.Data, "inputMuted")));
        }
        return result;
    }

    private async Task ReadLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        while (true)
        {
            using var msg = await ReceiveAsync(ws, ct);
            if (msg is null) return;
            var root = msg.RootElement;
            switch (Op(msg))
            {
                case 7: // RequestResponse
                    var d = root.GetProperty("d");
                    if (Prop(d, "requestId") is { } id && _requests.TryRemove(id, out var tcs)) tcs.TrySetResult(d.Clone());
                    break;
                case 5: // Event
                    var e = root.GetProperty("d");
                    var data = e.TryGetProperty("eventData", out var ed) ? ed.Clone() : default;
                    Interlocked.Increment(ref _eventsSeen);
                    HandleEvent(Prop(e, "eventType") ?? "", data, ct);
                    break;
            }
        }
    }

    private void HandleEvent(string type, JsonElement d, CancellationToken ct)
    {
        switch (type)
        {
            case "CurrentSceneCollectionChanging":
                // Hacer peticiones durante el cambio de colección "puede provocar cierres" (protocol.md).
                _collectionChanging = true;
                break;
            case "CurrentSceneCollectionChanged":
                _collectionChanging = false;
                _ = ReloadAsync(ct);
                break;
            case "CurrentProgramSceneChanged":
                Update(s => s with { Scene = Prop(d, "sceneName") });
                break;
            case "SceneListChanged":
                if (d.TryGetProperty("scenes", out var scenes))
                {
                    var names = scenes.EnumerateArray().Select(x => Prop(x, "sceneName")).OfType<string>().Reverse().ToList();
                    Update(s => s with { Scenes = names });
                }
                break;
            case "SceneCreated" or "SceneRemoved" or "SceneNameChanged" or "InputCreated" or "InputRemoved" or "InputNameChanged":
                // Se agrupan: una recarga a la vez (en un cambio de colección llegan decenas).
                if (!_collectionChanging) _ = ReloadAsync(ct);
                break;
            case "InputMuteStateChanged":
                var input = Prop(d, "inputName");
                var muted = Bool(d, "inputMuted");
                Update(s => s with { Inputs = s.Inputs.Select(i => i.Name == input ? i with { Muted = muted } : i).ToList() });
                break;
            case "RecordStateChanged":
                // outputActive en el evento es false en PAUSED/STARTING/STOPPING: el estado sale de outputState.
                var rs = Prop(d, "outputState") ?? "";
                Update(s => rs switch
                {
                    _ when rs.EndsWith("_STARTED", StringComparison.Ordinal) || rs.EndsWith("_RESUMED", StringComparison.Ordinal) =>
                        s with { Recording = true, RecordingPaused = false },
                    _ when rs.EndsWith("_PAUSED", StringComparison.Ordinal) => s with { Recording = true, RecordingPaused = true },
                    _ when rs.EndsWith("_STOPPED", StringComparison.Ordinal) => s with { Recording = false, RecordingPaused = false },
                    _ => s, // STARTING / STOPPING / RECONNECTING: se mantiene
                });
                break;
            case "StreamStateChanged":
                var ss = Prop(d, "outputState") ?? "";
                Update(s => ss switch
                {
                    _ when ss.EndsWith("_STARTED", StringComparison.Ordinal) || ss.EndsWith("_RECONNECTED", StringComparison.Ordinal) => s with { Streaming = true },
                    _ when ss.EndsWith("_STOPPED", StringComparison.Ordinal) => s with { Streaming = false },
                    _ => s,
                });
                break;
            case "ExitStarted":
                if (_ws is { } ws) ws.Abort();
                break;
        }

        List<(Waiter W, (bool Ok, string Message) R)> done = [];
        lock (_lock)
        {
            _waiters.RemoveAll(w => DateTime.UtcNow - w.Since > WaiterLifetime);
            foreach (var w in _waiters.ToList())
            {
                if (w.Match(type, d) is { } r)
                {
                    _waiters.Remove(w);
                    done.Add((w, r));
                }
            }
        }
        foreach (var (w, r) in done)
        {
            OperationFinished?.Invoke(new AgentOperation(w.OperationId, r.Ok ? "completed" : "failed", r.Ok ? null : ErrorCodes.Internal, r.Message));
        }
    }

    private async Task<(bool Ok, int Code, string? Comment, JsonElement Data)> RequestAsync(string type, JsonObject? data, CancellationToken ct)
    {
        var ws = _ws ?? throw new IOException("OBS no está conectado.");
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _requests[id] = tcs;
        var d = new JsonObject { ["requestType"] = type, ["requestId"] = id };
        if (data is not null) d["requestData"] = data;
        try
        {
            await SendAsync(ws, new JsonObject { ["op"] = 6, ["d"] = d }, ct);
            var response = await tcs.Task.WaitAsync(RequestTimeout, ct);
            var status = response.GetProperty("requestStatus");
            var ok = status.GetProperty("result").GetBoolean();
            var code = status.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
            var comment = Prop(status, "comment");
            var responseData = response.TryGetProperty("responseData", out var rd) ? rd : default;
            return (ok, code, comment, responseData);
        }
        finally
        {
            _requests.TryRemove(id, out _);
        }
    }

    private async Task SendAsync(ClientWebSocket ws, JsonObject message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _send.WaitAsync(ct);
        try
        {
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _send.Release();
        }
    }

    private static async Task<JsonDocument?> ReceiveAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await ws.ReceiveAsync(buffer, ct);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, r.Count);
            if (ms.Length > 4 * 1024 * 1024) throw new InvalidDataException("Mensaje de OBS demasiado grande.");
            if (r.EndOfMessage) return JsonDocument.Parse(ms.ToArray());
        }
    }

    // --- Utilidades ------------------------------------------------------------

    /// <summary>base64(sha256(base64(sha256(password + salt)) + challenge)).</summary>
    public static string AuthString(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    private static Waiter OutputWaiter(string id, string eventType, string done, string okMessage, string? stoppedMeansFailure) =>
        new(id, (type, d) =>
        {
            if (type != eventType) return null;
            var state = Prop(d, "outputState") ?? "";
            if (state.EndsWith("_" + done, StringComparison.Ordinal)) return (true, okMessage);
            if (stoppedMeansFailure is not null && state.EndsWith("_STOPPED", StringComparison.Ordinal)) return (false, stoppedMeansFailure);
            return null;
        }, DateTime.UtcNow);

    private static Waiter StateWaiter(string id, string done, string okMessage) =>
        new(id, (type, d) =>
        {
            if (type != "RecordStateChanged") return null;
            var state = Prop(d, "outputState") ?? "";
            if (state.EndsWith("_" + done, StringComparison.Ordinal)) return (true, okMessage);
            if (state.EndsWith("_STOPPED", StringComparison.Ordinal)) return (false, "La grabación se ha detenido.");
            return null;
        }, DateTime.UtcNow);

    private static string MapCode(int obsCode) => obsCode switch
    {
        600 => ErrorCodes.NotFound,                 // ResourceNotFound
        >= 500 and <= 504 => ErrorCodes.Conflict,   // OutputRunning / NotRunning / Paused / NotPaused / Disabled
        207 => ErrorCodes.ObsNotReady,              // NotReady (obs-websocket ≥ 5.3)
        _ => ErrorCodes.Internal,
    };

    private void FailWaiters(string message)
    {
        List<Waiter> pending;
        lock (_lock)
        {
            pending = [.. _waiters];
            _waiters.Clear();
        }
        foreach (var w in pending) OperationFinished?.Invoke(new AgentOperation(w.OperationId, "failed", ErrorCodes.ObsNotReady, message));
    }

    private void Update(Func<ObsInfo, ObsInfo> change)
    {
        lock (_lock) _state = change(_state);
        Changed?.Invoke();
    }

    private void SetState(ObsInfo next)
    {
        bool changed;
        lock (_lock)
        {
            changed = !Equivalent(_state, next);
            _state = next;
        }
        if (changed) Changed?.Invoke();
    }

    private static bool Equivalent(ObsInfo a, ObsInfo b) =>
        a.State == b.State && a.Recording == b.Recording && a.RecordingPaused == b.RecordingPaused && a.Streaming == b.Streaming &&
        a.Scene == b.Scene && a.Scenes.SequenceEqual(b.Scenes) && a.Inputs.SequenceEqual(b.Inputs);

    private static int Op(JsonDocument doc) => doc.RootElement.TryGetProperty("op", out var op) ? op.GetInt32() : -1;

    private static string? Prop(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public void Dispose()
    {
        _send.Dispose();
        _reload.Dispose();
    }
}

internal sealed class ObsRejectedException(string state) : Exception($"OBS rechazó la conexión ({state}).")
{
    public string State { get; } = state;
}
