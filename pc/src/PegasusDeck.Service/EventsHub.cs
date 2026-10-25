using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PegasusDeck.Core.Contract;
using PegasusDeck.Core.Security;
using PegasusDeck.Service.Agent;

namespace PegasusDeck.Service;

/// <summary>
/// WebSocket /api/events. El cliente se autentica con el primer mensaje (máx. 3 s);
/// después recibe la instantánea ante cada cambio y como latido. Si su token se
/// revoca, se le cierra la conexión con 4401.
/// Robustez: cada envío tiene tiempo máximo y se hace en paralelo (un cliente lento
/// no bloquea a los demás), un fallo puntual nunca detiene el servicio y hay un tope
/// de conexiones pendientes de autenticar.
/// </summary>
public sealed class EventsHub(
    SystemState state,
    DeviceStore devices,
    ServiceEvents events,
    IOptions<PegasusOptions> options,
    ILogger<EventsHub> log) : BackgroundService
{
    private static readonly TimeSpan AuthTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(3);
    private const int MaxClientMessage = 4096;
    private const int MaxPendingAuth = 8;
    private const int MaxClients = 16;

#pragma warning disable CA1001 // El semáforo vive lo mismo que el cliente; liberarlo abriría carreras con el latido.
    private sealed class Client(WebSocket ws, string deviceId)
#pragma warning restore CA1001
    {
        // Un envío a la vez por socket. No se libera nunca a mano: así no hay carreras
        // entre el cierre de la conexión y un latido en curso.
        private readonly SemaphoreSlim _send = new(1, 1);
        public WebSocket Socket => ws;
        public string DeviceId => deviceId;

        public async Task<bool> SendAsync(byte[] bytes, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(SendTimeout);
            var entered = false;
            try
            {
                await _send.WaitAsync(timeout.Token);
                entered = true;
                if (ws.State != WebSocketState.Open) return false;
                await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, timeout.Token);
                return true;
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Cliente que no lee o ya cerrado: se corta para no retener a los demás.
                ws.Abort();
                return false;
            }
            finally
            {
                if (entered) _send.Release();
            }
        }
    }

    private readonly ConcurrentDictionary<Guid, Client> _clients = new();
    private readonly SemaphoreSlim _pendingAuth = new(MaxPendingAuth, MaxPendingAuth);

    public int ClientCount => _clients.Count;

    public async Task HandleAsync(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            await JsonResults.Error(ErrorCodes.BadParams, "Se esperaba una conexión WebSocket.").ExecuteAsync(ctx);
            return;
        }
        // Tope de conexiones sin autenticar y de clientes: se rechaza antes de aceptar el WebSocket.
        if (_clients.Count >= MaxClients || !_pendingAuth.Wait(0))
        {
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        WebSocket ws;
        Core.Security.Device? device;
        try
        {
            ws = await ctx.WebSockets.AcceptWebSocketAsync();
            device = devices.FindByToken(await ReadAuthTokenAsync(ws, ctx.RequestAborted));
        }
        finally
        {
            _pendingAuth.Release();
        }

        using (ws)
        {
            if (device is null)
            {
                Log.EventsAuthRejected(log);
                await RejectAsync(ws, ctx.RequestAborted);
                return;
            }

            var id = Guid.NewGuid();
            var client = new Client(ws, device.DeviceId);
            _clients[id] = client;
            Log.EventsOpened(log, device.DeviceId);
            try
            {
                await client.SendAsync(Serialize(SnapshotMessage.Ready(state.Build())), ctx.RequestAborted);
                // El cliente no envía nada más: se descarta lo que llegue hasta el cierre.
                var buffer = new byte[MaxClientMessage];
                while (ws.State == WebSocketState.Open)
                {
                    var r = await ws.ReceiveAsync(buffer, ctx.RequestAborted);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                }
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException)
            {
                // desconexión del cliente
            }
            finally
            {
                _clients.TryRemove(id, out _);
                if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await TryCloseAsync(ws, WebSocketCloseStatus.NormalClosure, "bye");
                }
                Log.EventsClosed(log, device.DeviceId);
            }
        }
    }

    /// <summary>Envía un mensaje a todos los clientes autenticados, en paralelo.</summary>
    public async Task BroadcastAsync(object message, CancellationToken ct)
    {
        var bytes = Serialize(message);
        var targets = _clients.ToArray();
        var results = await Task.WhenAll(targets.Select(kv => kv.Value.SendAsync(bytes, ct)));
        for (var i = 0; i < targets.Length; i++)
        {
            if (!results[i]) _clients.TryRemove(targets[i].Key, out _);
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(HeartbeatLoopAsync(stoppingToken), EventsLoopAsync(stoppingToken));

    /// <summary>P-2: cambios de estado (agrupados 100 ms) y resultados de operaciones, al momento.</summary>
    private async Task EventsLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in events.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    if (item is ServiceEvents.SnapshotDirty)
                    {
                        await Task.Delay(100, stoppingToken);
                        while (events.Reader.TryPeek(out var next) && next is ServiceEvents.SnapshotDirty) events.Reader.TryRead(out _);
                        await BroadcastAsync(SnapshotMessage.Update(state.Build()), stoppingToken);
                    }
                    else
                    {
                        await BroadcastAsync(item, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
#pragma warning disable CA1031 // Un fallo al enviar un evento no debe parar el servicio.
                catch (Exception e)
#pragma warning restore CA1031
                {
                    Log.HeartbeatFailed(log, e);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.HeartbeatSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await HeartbeatAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // Un fallo puntual (p. ej. al leer devices.json) no debe detener el servicio.
            catch (Exception e)
#pragma warning restore CA1031
            {
                Log.HeartbeatFailed(log, e);
            }
        }
    }

    private async Task HeartbeatAsync(CancellationToken ct)
    {
        // Tokens revocados desde el PC: fuera.
        foreach (var (id, c) in _clients)
        {
            if (devices.Exists(c.DeviceId)) continue;
            _clients.TryRemove(id, out _);
            Log.DeviceRevoked(log, c.DeviceId);
            await c.SendAsync(Serialize(new ErrorMessage(ErrorCodes.AuthRequired, "Este dispositivo ha sido revocado en el PC.")), ct);
            await TryCloseAsync(c.Socket, (WebSocketCloseStatus)Protocol.WsCloseAuth, "revoked");
        }
        await BroadcastAsync(SnapshotMessage.Update(state.Build()), ct);
    }

    private static byte[] Serialize(object message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), JsonDefaults.Options);

    private static async Task<string?> ReadAuthTokenAsync(WebSocket ws, CancellationToken aborted)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        cts.CancelAfter(AuthTimeout);
        var text = await ReceiveTextAsync(ws, cts.Token);
        if (text is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            return RequestParser.ParseAuthToken(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task RejectAsync(WebSocket ws, CancellationToken ct)
    {
        if (ws.State == WebSocketState.Open)
        {
            var bytes = Serialize(new ErrorMessage(ErrorCodes.AuthRequired, "Token no válido o revocado."));
            try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
        }
        await TryCloseAsync(ws, (WebSocketCloseStatus)Protocol.WsCloseAuth, "auth");
    }

    private static async Task TryCloseAsync(WebSocket ws, WebSocketCloseStatus status, string reason)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await ws.CloseOutputAsync(status, reason, cts.Token);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // ya estaba cerrado
        }
    }

    /// <summary>Lee un mensaje de texto completo (máx. 4 KB); null si no llega a tiempo o no es válido.</summary>
    private static async Task<string?> ReceiveTextAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[MaxClientMessage];
        var total = 0;
        try
        {
            while (true)
            {
                if (total >= buffer.Length) return null;
                var r = await ws.ReceiveAsync(buffer.AsMemory(total), ct);
                if (r.MessageType != WebSocketMessageType.Text) return null;
                total += r.Count;
                if (r.EndOfMessage) return Encoding.UTF8.GetString(buffer, 0, total);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException)
        {
            return null;
        }
    }

    public override void Dispose()
    {
        _pendingAuth.Dispose();
        base.Dispose();
    }
}
