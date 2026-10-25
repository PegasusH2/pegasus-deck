using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PegasusDeck.Agent.Obs;

namespace PegasusDeck.Tests;

/// <summary>
/// OBS simulado que habla obs-websocket 5 (Hello/Identify/Identified, Request/Response,
/// Event) con el subconjunto que usa Pegasus Deck, imitando a obs-websocket 5.2.3 / OBS 29:
///  - outputActive en los eventos solo es true en STARTED/RESUMED/RECONNECTED (GetOutputStateActive);
///    en PAUSED, STARTING y STOPPING es false. GetRecordStatus sí da outputActive=true en pausa.
///  - Fallos sin evento: grabar con carpeta/espacio/codificador no válidos y pausar con codificador compartido.
/// </summary>
public sealed class FakeObs : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();
    private readonly object _lock = new();

    public int Port { get; private set; }
    public string? Password { get; set; }
    public int Connections;
    public List<string> Scenes { get; } = ["Juego", "Charla"];
    public string CurrentScene { get; set; } = "Juego";
    public Dictionary<string, bool> AudioInputs { get; } = new() { ["Mic/Aux"] = false };
    public bool Recording { get; set; }
    public bool Paused { get; set; }
    public bool Streaming { get; set; }
    public bool FailStartStream { get; set; }
    /// <summary>StartRecord responde bien pero OBS no graba (solo un aviso en el PC; sin evento).</summary>
    public bool SilentStartRecordFailure { get; set; }
    /// <summary>PauseRecord responde bien pero no pausa (codificador compartido con la emisión; sin evento).</summary>
    public bool PauseUnsupported { get; set; }
    public bool EventBeforeResponse { get; set; }
    public bool DropEvents { get; set; }
    public int Requests;

    private FakeObs(WebApplication app) => _app = app;

    public static async Task<FakeObs> StartAsync(string? password)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var fake = new FakeObs(app) { Password = password };
        app.UseWebSockets();
        app.Run(ctx => fake.HandleAsync(ctx));
        await app.StartAsync();
        fake.Port = new Uri(app.Urls.First()).Port;
        return fake;
    }

    /// <summary>Simula que OBS corta todas las conexiones (p. ej. al cerrarse).</summary>
    public void DropAll()
    {
        foreach (var ws in _sockets.Values) ws.Abort();
    }

    /// <summary>Cambio de colección de escenas como lo notifica OBS: Changing, cambios sueltos, Changed.</summary>
    public async Task ChangeSceneCollectionAsync(string[] scenes, string current)
    {
        await Broadcast("CurrentSceneCollectionChanging", new JsonObject { ["sceneCollectionName"] = "Antigua" });
        lock (_lock)
        {
            Scenes.Clear();
            Scenes.AddRange(scenes);
            CurrentScene = current;
        }
        for (var i = 0; i < 5; i++) await Broadcast("InputRemoved", new JsonObject { ["inputName"] = $"Fuente {i}" });
        await Broadcast("CurrentSceneCollectionChanged", new JsonObject { ["sceneCollectionName"] = "Nueva" });
    }

    private async Task Broadcast(string type, JsonObject data)
    {
        foreach (var ws in _sockets.Values) await Send(ws, 5, new JsonObject { ["eventType"] = type, ["eventIntent"] = 2, ["eventData"] = data });
    }

    private async Task HandleAsync(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
        using var ws = await ctx.WebSockets.AcceptWebSocketAsync("obswebsocket.json");
        Interlocked.Increment(ref Connections);
        var id = Guid.NewGuid();
        _sockets[id] = ws;
        try
        {
            var salt = "c2FsdA==";
            var challenge = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            var hello = new JsonObject { ["obsWebSocketVersion"] = "5.2.3", ["rpcVersion"] = 1 };
            if (Password is not null) hello["authentication"] = new JsonObject { ["challenge"] = challenge, ["salt"] = salt };
            await Send(ws, 0, hello);

            var identify = await Receive(ws);
            if (identify is null) return;
            var d = identify["d"]!;
            if (Password is not null && (string?)d["authentication"] != ObsClient.AuthString(Password, salt, challenge))
            {
                await ws.CloseAsync((WebSocketCloseStatus)4009, "Authentication failed.", CancellationToken.None);
                return;
            }
            await Send(ws, 2, new JsonObject { ["negotiatedRpcVersion"] = 1 });

            while (ws.State == WebSocketState.Open)
            {
                var msg = await Receive(ws);
                if (msg is null) break;
                if ((int)msg["op"]! == 6) await HandleRequest(ws, msg["d"]!);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException)
        {
        }
        finally
        {
            _sockets.TryRemove(id, out _);
        }
    }

    private async Task HandleRequest(WebSocket ws, JsonNode d)
    {
        Interlocked.Increment(ref Requests);
        var type = (string)d["requestType"]!;
        var requestId = (string)d["requestId"]!;
        var data = d["requestData"];
        var events = new List<(string Type, JsonObject Data)>();
        void Record(string state) => events.Add(("RecordStateChanged", Output(state)));
        void Stream(string state) => events.Add(("StreamStateChanged", Output(state)));
        (bool Ok, int Code, string? Comment, JsonObject? Response) r;
        lock (_lock)
        {
            r = type switch
            {
                "GetSceneList" => (true, 100, null, new JsonObject
                {
                    ["currentProgramSceneName"] = CurrentScene,
                    // OBS devuelve las escenas de abajo arriba
                    ["scenes"] = new JsonArray(Scenes.AsEnumerable().Reverse().Select((n, i) => (JsonNode)new JsonObject { ["sceneName"] = n, ["sceneIndex"] = i }).ToArray()),
                }),
                "GetInputList" => (true, 100, null, new JsonObject
                {
                    ["inputs"] = new JsonArray(AudioInputs.Keys.Select(n => (JsonNode)new JsonObject { ["inputName"] = n, ["inputKind"] = "wasapi_input_capture" })
                        .Append(new JsonObject { ["inputName"] = "Cámara", ["inputKind"] = "dshow_input" }).ToArray()),
                }),
                "GetInputMute" => AudioInputs.TryGetValue((string)data!["inputName"]!, out var m)
                    ? (true, 100, null, new JsonObject { ["inputMuted"] = m })
                    : (false, 604, "La fuente no tiene audio.", null),
                // En pausa, GetRecordStatus mantiene outputActive=true (obs_output_active).
                "GetRecordStatus" => (true, 100, null, new JsonObject { ["outputActive"] = Recording, ["outputPaused"] = Paused }),
                "GetStreamStatus" => (true, 100, null, new JsonObject { ["outputActive"] = Streaming }),
                "StartRecord" when Recording => (false, 500, "Ya se está grabando.", null),
                "StartRecord" when SilentStartRecordFailure => (true, 100, null, null),
                "StartRecord" => Do(() => { Recording = true; Record("STARTING"); Record("STARTED"); }),
                "StopRecord" when !Recording => (false, 501, "No se está grabando.", null),
                "StopRecord" => Do(() => { Recording = false; Paused = false; Record("STOPPING"); Record("STOPPED"); }),
                "PauseRecord" when !Recording => (false, 501, "No se está grabando.", null),
                "PauseRecord" when Paused => (false, 502, "La grabación ya está en pausa.", null),
                "PauseRecord" when PauseUnsupported => (true, 100, null, null),
                "PauseRecord" => Do(() => { Paused = true; Record("PAUSED"); }),
                "ResumeRecord" when !Paused => (false, 503, "La grabación no está en pausa.", null),
                "ResumeRecord" => Do(() => { Paused = false; Record("RESUMED"); }),
                "StartStream" when FailStartStream => Do(() => { Stream("STARTING"); Stream("STOPPED"); }),
                "StartStream" => Do(() => { Streaming = true; Stream("STARTING"); Stream("STARTED"); }),
                "StopStream" => Do(() => { Streaming = false; Stream("STOPPING"); Stream("STOPPED"); }),
                "SetCurrentProgramScene" when !Scenes.Contains((string)data!["sceneName"]!) => (false, 600, "No existe la escena.", null),
                "SetCurrentProgramScene" => Do(() => { CurrentScene = (string)data!["sceneName"]!; events.Add(("CurrentProgramSceneChanged", new JsonObject { ["sceneName"] = CurrentScene })); }),
                "ToggleInputMute" => Do(() =>
                {
                    var name = (string)data!["inputName"]!;
                    AudioInputs[name] = !AudioInputs[name];
                    events.Add(("InputMuteStateChanged", new JsonObject { ["inputName"] = name, ["inputMuted"] = AudioInputs[name] }));
                }),
                _ => (false, 204, "Petición desconocida.", null),
            };
        }

        var response = new JsonObject
        {
            ["requestType"] = type,
            ["requestId"] = requestId,
            ["requestStatus"] = new JsonObject { ["result"] = r.Ok, ["code"] = r.Code, ["comment"] = r.Comment },
        };
        if (r.Response is not null) response["responseData"] = r.Response;

        if (EventBeforeResponse) await SendEvents(ws, events);
        await Send(ws, 7, response);
        if (!EventBeforeResponse) await SendEvents(ws, events);
    }

    private async Task SendEvents(WebSocket ws, List<(string Type, JsonObject Data)> events)
    {
        if (DropEvents) return;
        foreach (var (t, data) in events)
        {
            await Task.Delay(30);
            await Send(ws, 5, new JsonObject { ["eventType"] = t, ["eventIntent"] = 64, ["eventData"] = data });
        }
    }

    private static (bool, int, string?, JsonObject?) Do(Action a)
    {
        a();
        return (true, 100, null, null);
    }

    /// <summary>Como GetOutputStateActive de obs-websocket 5.2.3.</summary>
    private static JsonObject Output(string state) =>
        new() { ["outputActive"] = state is "STARTED" or "RESUMED" or "RECONNECTED", ["outputState"] = "OBS_WEBSOCKET_OUTPUT_" + state };

    private static async Task Send(WebSocket ws, int op, JsonObject d)
    {
        var bytes = Encoding.UTF8.GetBytes(new JsonObject { ["op"] = op, ["d"] = d }.ToJsonString());
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonNode?> Receive(WebSocket ws)
    {
        var buffer = new byte[64 * 1024];
        var r = await ws.ReceiveAsync(buffer, CancellationToken.None);
        return r.MessageType == WebSocketMessageType.Close ? null : JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, r.Count));
    }

    public async ValueTask DisposeAsync()
    {
        DropAll();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
