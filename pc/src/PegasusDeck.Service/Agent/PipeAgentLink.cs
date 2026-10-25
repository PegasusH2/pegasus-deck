using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PegasusDeck.Core.Agent;
using PegasusDeck.Core.Apps;
using PegasusDeck.Core.Contract;
using PegasusDeck.Core.Native;

namespace PegasusDeck.Service.Agent;

/// <summary>
/// Canal con el agente de la sesión del usuario (ADR-016). El servicio es el servidor
/// del named pipe; solo acepta como agente al ejecutable de confianza (por ruta) y le
/// entrega la lista blanca de apps. Un agente a la vez.
/// </summary>
public sealed class PipeAgentLink(
    IOptions<PegasusOptions> options,
    OperationTracker tracker,
    ServiceEvents events,
    ILogger<PipeAgentLink> log) : BackgroundService, IAgentLink
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);
    /// <summary>Mayor que los límites internos del agente (3 s con OBS): el agente responde antes de que el servicio desista.</summary>
    private static readonly TimeSpan ResultTimeout = TimeSpan.FromSeconds(8);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<AgentResult>> _results = new(StringComparer.Ordinal);
    private int _rejections;
    private DateTimeOffset _lastRejectionLog = DateTimeOffset.MinValue;
    private int _rejectionsSuppressed;
    private volatile AgentState _state = AgentState.None;
    private volatile LineChannel? _channel;

    public AgentState Current => _state;

    public bool Connected => _channel is not null;

    public async Task<ActionOutcome> ExecuteAsync(ActionRequest request, string operationId, CancellationToken ct)
    {
        var channel = _channel;
        if (channel is null || !_state.SessionReady)
        {
            return new ActionOutcome.Failed(ErrorCodes.NoSession, "No hay una sesión de Windows con el agente de Pegasus Deck en marcha.");
        }
        var tcs = new TaskCompletionSource<AgentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _results[operationId] = tcs;
        try
        {
            await channel.SendAsync(new ExecuteMessage(operationId, request.Type, request.Params), ct);
            var r = await tcs.Task.WaitAsync(ResultTimeout, ct);
            return r.Status switch
            {
                "completed" => new ActionOutcome.Completed(Clip(r.Message)),
                "accepted" => new ActionOutcome.Accepted(operationId, Clip(r.Message)),
                _ => new ActionOutcome.Failed(SafeCode(r.Code), Clip(r.Message)),
            };
        }
        catch (TimeoutException)
        {
            return new ActionOutcome.Failed(ErrorCodes.Timeout, "El agente de sesión no ha respondido.");
        }
        catch (IOException)
        {
            return new ActionOutcome.Failed(ErrorCodes.NoSession, "Se ha perdido la conexión con el agente de sesión.");
        }
        finally
        {
            _results.TryRemove(operationId, out _);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ServeOneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // Un fallo con un agente no debe parar el servicio: se espera al siguiente.
            catch (Exception e)
#pragma warning restore CA1031
            {
                Log.AgentFailed(log, e);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    private async Task ServeOneAsync(CancellationToken ct)
    {
        await using var pipe = CreatePipe(options.Value.AgentPipeName);
        await pipe.WaitForConnectionAsync(ct);

        var pid = ProcessIdentity.ClientProcessId(pipe);
        var path = pid is { } p ? ProcessIdentity.ImagePath(p) : null;
        var problem = !IsTrustedAgent(path) ? $"no es el agente de confianza ({path ?? "desconocido"})"
            : !IsActiveConsoleSession(pipe) ? "no está en la sesión activa de la consola"
            : null;
        if (problem is not null)
        {
            await RejectAsync(problem, ct);
            return;
        }

        using var channel = new LineChannel(pipe);
        using (var hs = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            hs.CancelAfter(HandshakeTimeout);
            using var first = await channel.ReceiveAsync(hs.Token);
            var hello = first is not null && LineChannel.TypeOf(first) == "hello" ? LineChannel.As<AgentHello>(first) : null;
            if (hello is null || hello.Protocol != AgentProtocol.Version)
            {
                await RejectAsync("saludo no válido", ct);
                return;
            }
            var (apps, errors) = AppsFile.Load(options.Value.DataDir);
            foreach (var e in errors) Log.AppsFileProblem(log, e);
            await channel.SendAsync(new ServiceHello(AgentProtocol.Version, apps), ct);
            // La sesión la da Windows (GetNamedPipeClientSessionId), no la que declara el agente.
            Log.AgentConnected(log, ProcessIdentity.ClientSessionId(pipe) ?? -1, apps.Count);
        }

        _channel = channel;
        _rejections = 0;
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var watchdog = WatchSessionAsync(pipe, connection);
        var appsWatch = WatchAppsFileAsync(channel, connection.Token);
        try
        {
            while (true)
            {
                using var doc = await channel.ReceiveAsync(connection.Token);
                if (doc is null) break;
                if (!Handle(doc)) throw new InvalidDataException("Mensaje del agente no válido.");
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException)
        {
            Log.AgentFailed(log, e);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.AgentLeftConsole(log); // cambio rápido de usuario: el agente ya no está en la consola
        }
        finally
        {
            await connection.CancelAsync();
            try { await watchdog; } catch (OperationCanceledException) { }
            try { await appsWatch; } catch (OperationCanceledException) { }
            _channel = null;
            _state = AgentState.None;
            foreach (var (id, tcs) in _results)
            {
                tcs.TrySetResult(AgentResult.Failed(id, ErrorCodes.NoSession, "Se ha perdido la conexión con el agente de sesión."));
            }
            tracker.FailAll(ErrorCodes.NoSession, "El agente de sesión se ha desconectado antes de confirmar la acción.");
            events.SnapshotChanged();
            Log.AgentDisconnected(log);
        }
    }

    /// <summary>
    /// Si el usuario cambia (cambio rápido de usuario), el agente conectado deja de estar en la consola:
    /// se le corta para que las órdenes del iPad no se ejecuten en una sesión que nadie ve.
    /// </summary>
    private async Task WatchSessionAsync(NamedPipeServerStream pipe, CancellationTokenSource connection)
    {
        if (options.Value.TrustedAgentPath == "*") return; // desarrollo
        var session = ProcessIdentity.ClientSessionId(pipe);
        while (!connection.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), connection.Token);
            if (session != ProcessIdentity.ActiveConsoleSessionId())
            {
                await connection.CancelAsync();
                return;
            }
        }
    }

    /// <summary>
    /// Si apps.json cambia (menú "Aplicaciones autorizadas…" del agente u otro administrador), se envía la
    /// lista nueva al agente sin reiniciar nada (ADR-023). Se comprueba cada 2 s: fecha y tamaño del archivo.
    /// </summary>
    private async Task WatchAppsFileAsync(LineChannel channel, CancellationToken ct)
    {
        var path = Path.Combine(options.Value.DataDir, AppsFile.FileName);
        var last = Stamp(path);
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(AppsPoll, ct);
            var now = Stamp(path);
            if (now == last) continue;
            last = now;
            var (apps, errors) = AppsFile.Load(options.Value.DataDir);
            foreach (var e in errors) Log.AppsFileProblem(log, e);
            try
            {
                await channel.SendAsync(new ServiceApps(apps), ct);
                Log.AppsReloaded(log, apps.Count);
            }
            catch (IOException)
            {
                return; // el canal se ha cerrado: la lista irá en el próximo saludo
            }
        }

        static (DateTime, long) Stamp(string p)
        {
            var f = new FileInfo(p);
            return f.Exists ? (f.LastWriteTimeUtc, f.Length) : (DateTime.MinValue, -1);
        }
    }

    /// <summary>Avisa al agente de que un iPad se ha emparejado (su ventana de emparejamiento lo muestra).</summary>
    public void NotifyPaired(string deviceName)
    {
        if (_channel is not { } channel) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await channel.SendAsync(new ServicePaired(deviceName), CancellationToken.None);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                // sin agente: solo se pierde el aviso visual
            }
        });
    }

    /// <summary>Intervalo de comprobación de apps.json (más corto en pruebas).</summary>
    internal TimeSpan AppsPoll { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Procesa un mensaje; false si no es válido (se corta la conexión).</summary>
    private bool Handle(JsonDocument doc)
    {
        switch (LineChannel.TypeOf(doc))
        {
            case "state":
                var s = LineChannel.As<AgentStateMessage>(doc);
                if (s?.Obs?.State is null || s.Obs.Scenes is null || s.Obs.Inputs is null || s.Apps is null || s.Audio?.State is null || s.AppsDetection is null)
                {
                    return false;
                }
                // Se normaliza ya aquí: el estado guardado siempre cumple el contrato.
                var clean = SnapshotLimits.Clamp(new Snapshot(Protocol.Version, new ServiceInfo(Protocol.ServiceName, "0"),
                    new WindowsInfo(true, "", 0), new SessionInfo(s.SessionReady), s.Obs, s.Apps, s.AppsDetection, s.Audio, s.Mic, s.Media, s.Outputs));
                _state = new AgentState(s.SessionReady, clean.Obs, clean.Apps, clean.AppsDetection, clean.Audio, clean.Mic, clean.Media, clean.Outputs);
                events.SnapshotChanged();
                return true;
            case "result":
                if (LineChannel.As<AgentResult>(doc) is not { OperationId: not null, Status: not null } r) return false;
                if (_results.TryGetValue(r.OperationId, out var tcs)) tcs.TrySetResult(r);
                return true;
            case "operation":
                if (LineChannel.As<AgentOperation>(doc) is not { OperationId: not null, Status: not null } op) return false;
                tracker.Complete(op);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// El agente debe estar en la sesión de la consola activa: si hay varios usuarios con
    /// sesión abierta, las órdenes del iPad no deben ejecutarse en la sesión de otro.
    /// </summary>
    private bool IsActiveConsoleSession(NamedPipeServerStream pipe)
    {
        if (options.Value.TrustedAgentPath == "*") return true; // desarrollo
        var session = ProcessIdentity.ClientSessionId(pipe);
        return session is not null && session == ProcessIdentity.ActiveConsoleSessionId();
    }

    /// <summary>Rechazo con espera creciente (0,25 → 5 s) y registro agrupado: sin bucles ni avalanchas de avisos.</summary>
    private async Task RejectAsync(string problem, CancellationToken ct)
    {
        _rejections++;
        var now = DateTimeOffset.UtcNow;
        if (now - _lastRejectionLog >= TimeSpan.FromMinutes(1))
        {
            Log.AgentRejected(log, problem + (_rejectionsSuppressed > 0 ? $" (+{_rejectionsSuppressed} rechazos más)" : ""));
            _lastRejectionLog = now;
            _rejectionsSuppressed = 0;
        }
        else
        {
            _rejectionsSuppressed++;
        }
        var delay = TimeSpan.FromMilliseconds(Math.Min(5000, 250 * Math.Pow(2, Math.Min(_rejections - 1, 5))));
        await Task.Delay(delay, ct);
    }

    private bool IsTrustedAgent(string? path)
    {
        var trusted = options.Value.TrustedAgentPath;
        if (trusted == "*") return options.Value.UnprotectedDataDir; // solo desarrollo (validado al arrancar)
        return ProcessIdentity.SamePath(path, trusted);
    }

    /// <summary>Pipe con ACL: SYSTEM y Administradores, usuarios interactivos (el agente) y la cuenta del propio servicio.</summary>
    private static NamedPipeServerStream CreatePipe(string name)
    {
        var acl = new PipeSecurity();
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        using var me = WindowsIdentity.GetCurrent();
        if (me.User is { } user)
        {
            acl.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
            // El agente comprueba que el propietario es la cuenta del servicio (SYSTEM, LocalService…).
            acl.SetOwner(user);
        }
        // FirstPipeInstance: si otro proceso creó ya el pipe con este nombre, falla en vez de compartirlo.
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, acl);
    }

    private static readonly HashSet<string> KnownCodes =
        typeof(ErrorCodes).GetFields().Select(f => (string)f.GetValue(null)!).ToHashSet(StringComparer.Ordinal);

    private static string SafeCode(string? code) => code is not null && KnownCodes.Contains(code) ? code : ErrorCodes.Internal;

    private static string Clip(string? s) => s is null ? "" : s.Length > 300 ? s[..300] : s;
}
