using System.IO.Pipes;
using System.Text.Json;
using PegasusDeck.Core.Agent;
using PegasusDeck.Core.Apps;
using PegasusDeck.Core.Native;

namespace PegasusDeck.Agent;

/// <summary>
/// Cliente del named pipe del servicio. Antes de confiar en el otro extremo comprueba
/// que es el ejecutable del servicio de confianza: un proceso que se hiciera pasar por
/// el servicio podría enviar una lista blanca falsa.
/// </summary>
internal sealed class ServiceConnection(AgentOptions options, IAgentHandler handler)
{
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    private volatile LineChannel? _channel;

    public event Action<string>? StatusChanged;

    /// <summary>El servicio confirma que un iPad se ha emparejado (nombre del dispositivo).</summary>
    public event Action<string>? Paired;

    public bool Connected => _channel is not null;

    public async Task RunAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectOnceAsync(ct);
                attempt = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                StatusChanged?.Invoke($"Sin conexión con el servicio: {e.Message}");
            }
            catch (UntrustedServiceException e)
            {
                StatusChanged?.Invoke(e.Message);
            }
            await Task.Delay(Backoff[Math.Min(attempt++, Backoff.Length - 1)], ct);
        }
    }

    /// <summary>
    /// ¿Es el servicio de Windows de verdad? (ADR-016, revisado tras la fase 3)
    ///  - Producción: el servidor está en la sesión 0 (un usuario sin privilegios no puede crear procesos ahí)
    ///    y el propietario del pipe es una cuenta de servicio (no se puede falsificar sin ser administrador).
    ///    La ruta del ejecutable se comprueba solo si se puede leer: desde un usuario normal, Windows no deja
    ///    consultar procesos de SYSTEM.
    ///  - Desarrollo (--insecure-dev, solo Debug): basta con la ruta (el servicio de pruebas no es un servicio).
    /// </summary>
    private void VerifyServer(NamedPipeClientStream pipe)
    {
        var pid = ProcessIdentity.ServerProcessId(pipe);
        var path = pid is { } p ? ProcessIdentity.ImagePath(p) : null;
        if (options.InsecureDev)
        {
            var ok = options.TrustedServicePath == "*" || ProcessIdentity.SamePath(path, options.TrustedServicePath);
            if (!ok) throw new UntrustedServiceException($"El otro extremo del pipe no es el servicio de confianza ({path ?? "desconocido"}).");
            return;
        }
        if (ProcessIdentity.ServerSessionId(pipe) != 0)
        {
            throw new UntrustedServiceException("El otro extremo del pipe no es el servicio de Windows (no está en la sesión 0).");
        }
        if (!ProcessIdentity.IsServiceAccount(ProcessIdentity.PipeOwner(pipe)))
        {
            throw new UntrustedServiceException("El pipe no pertenece a la cuenta del servicio de Pegasus Deck.");
        }
        if (path is not null && !ProcessIdentity.SamePath(path, options.TrustedServicePath))
        {
            throw new UntrustedServiceException($"El otro extremo del pipe no es el servicio de confianza ({path}).");
        }
    }

    public Task SendAsync(object message, CancellationToken ct) =>
        _channel is { } c ? c.SendAsync(message, ct) : Task.CompletedTask;

    private async Task ConnectOnceAsync(CancellationToken ct)
    {
        await using var pipe = new NamedPipeClientStream(".", options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000, ct);

        VerifyServer(pipe);

        using var channel = new LineChannel(pipe);
        await channel.SendAsync(new AgentHello(AgentProtocol.Version, typeof(ServiceConnection).Assembly.GetName().Version?.ToString() ?? "0", Environment.ProcessId > 0 ? System.Diagnostics.Process.GetCurrentProcess().SessionId : 0), ct);

        using (var hs = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            hs.CancelAfter(TimeSpan.FromSeconds(5));
            using var first = await channel.ReceiveAsync(hs.Token) ?? throw new IOException("El servicio cerró la conexión.");
            var hello = LineChannel.TypeOf(first) == "hello" ? LineChannel.As<ServiceHello>(first) : null;
            if (hello is null || hello.Protocol != AgentProtocol.Version) throw new InvalidDataException("Saludo del servicio no válido.");
            // Defensa en profundidad: la lista blanca se vuelve a validar aquí.
            var (apps, _) = AppsFile.Validate(hello.Apps ?? []);
            handler.SetApps(apps);
        }

        _channel = channel;
        StatusChanged?.Invoke("Conectado con el servicio");
        await handler.OnConnectedAsync(ct);
        try
        {
            while (true)
            {
                using var doc = await channel.ReceiveAsync(ct);
                if (doc is null) break;
                if (LineChannel.TypeOf(doc) == "paired" && LineChannel.As<ServicePaired>(doc) is { DeviceName: not null } paired)
                {
                    Paired?.Invoke(LineChannel.Printable(paired.DeviceName));
                    continue;
                }
                if (LineChannel.TypeOf(doc) == "apps" && LineChannel.As<ServiceApps>(doc) is { Apps: not null } changed)
                {
                    // Lista blanca nueva (se vuelve a validar aquí, como en el saludo).
                    handler.SetApps(AppsFile.Validate(changed.Apps).Apps);
                    continue;
                }
                if (LineChannel.TypeOf(doc) == "execute" && LineChannel.As<ExecuteEnvelope>(doc) is { } exec)
                {
                    // Cada orden en su propia tarea: una acción lenta no bloquea la lectura.
                    _ = Task.Run(async () =>
                    {
                        var result = await handler.ExecuteAsync(exec, ct);
                        await SendAsync(result, ct);
                    }, ct);
                }
            }
        }
        finally
        {
            _channel = null;
            StatusChanged?.Invoke("Desconectado del servicio");
        }
    }
}

/// <summary>"execute" tal como llega por el pipe (parámetros sin tipar todavía).</summary>
internal sealed record ExecuteEnvelope(string OperationId, string Action, Dictionary<string, JsonElement>? Params);

internal interface IAgentHandler
{
    void SetApps(IReadOnlyList<AppEntry> apps);

    Task OnConnectedAsync(CancellationToken ct);

    Task<AgentResult> ExecuteAsync(ExecuteEnvelope request, CancellationToken ct);
}

internal sealed class UntrustedServiceException(string message) : Exception(message);
