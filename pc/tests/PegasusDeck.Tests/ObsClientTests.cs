using PegasusDeck.Agent.Obs;
using PegasusDeck.Core.Agent;
using PegasusDeck.Core.Contract;

namespace PegasusDeck.Tests;

/// <summary>Cliente obs-websocket 5 contra el OBS simulado (no se toca el OBS real del PC).</summary>
public sealed class ObsClientTests : IAsyncLifetime
{
    private FakeObs _obs = null!;
    private ObsClient _client = null!;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<AgentOperation> _finished = [];
    // El OBS simulado escucha en este mismo proceso: así pasa la comprobación real del dueño del puerto.
    private int? _pid = Environment.ProcessId;
    private string? _password = "secreto";
    private Task _run = Task.CompletedTask;

    public async Task InitializeAsync()
    {
        _obs = await FakeObs.StartAsync("secreto");
        _client = new ObsClient(() => new ObsSettings(_obs.Port, _password), () => _pid) { IdlePoll = TimeSpan.FromMilliseconds(100) };
        _client.OperationFinished += op => { lock (_finished) _finished.Add(op); };
    }

    public async Task DisposeAsync()
    {
        await _cts.CancelAsync();
        try { await _run; } catch (OperationCanceledException) { }
        _client.Dispose();
        _cts.Dispose();
        await _obs.DisposeAsync();
    }

    private void Start() => _run = _client.RunAsync(_cts.Token);

    private static async Task Until(Func<bool> condition, int timeoutMs = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("La condición no se cumplió a tiempo.");
            await Task.Delay(25);
        }
    }

    private async Task<AgentOperation> Finished(string id)
    {
        AgentOperation? op = null;
        await Until(() => { lock (_finished) op = _finished.FirstOrDefault(f => f.OperationId == id); return op is not null; });
        return op!;
    }

    private static readonly Dictionary<string, object> NoParams = [];
    private static readonly string[] NewScenes = ["Intro", "Directo", "Fin"];

    [Fact]
    public async Task Sin_proceso_de_OBS_no_se_conecta()
    {
        _pid = null;
        Start();
        await Task.Delay(300);
        Assert.Equal("unavailable", _client.State.State);
        Assert.Equal(0, _obs.Connections);
    }

    [Fact]
    public async Task No_se_habla_con_un_puerto_que_no_es_del_proceso_de_OBS()
    {
        _pid = 4; // un "obs64" que no es quien escucha en el puerto (p. ej. de otro usuario)
        Start();
        await Task.Delay(1500);
        Assert.Equal(0, _obs.Connections); // ni siquiera se le envía la prueba de la contraseña
        Assert.NotEqual("ready", _client.State.State);
    }

    [Fact]
    public async Task Autenticado_carga_el_estado_completo()
    {
        Start();
        await Until(() => _client.State.State == "ready");
        var s = _client.State;
        Assert.Equal(["Juego", "Charla"], s.Scenes);
        Assert.Equal("Juego", s.Scene);
        Assert.Equal(["Mic/Aux"], s.Inputs.Select(i => i.Name)); // "Cámara" no tiene audio
        Assert.False(s.Recording);
    }

    [Fact]
    public async Task Contrasena_incorrecta_da_auth_error_sin_reintentar_en_bucle()
    {
        _password = "otra";
        Start();
        await Until(() => _client.State.State == "auth_error");
        await Task.Delay(1500);
        Assert.Equal(1, _obs.Connections);

        _password = "secreto"; // al corregirla, se vuelve a intentar
        await Until(() => _client.State.State == "ready");
    }

    [Fact]
    public async Task Grabar_se_acepta_y_solo_el_evento_lo_confirma()
    {
        Start();
        await Until(() => _client.State.State == "ready");
        var r = await _client.ExecuteAsync("0000000000000001", "obs.recording.start", NoParams, _cts.Token);
        Assert.Equal("accepted", r.Status);
        var op = await Finished("0000000000000001");
        Assert.Equal("completed", op.Status);
        await Until(() => _client.State.Recording);

        var again = await _client.ExecuteAsync("0000000000000002", "obs.recording.start", NoParams, _cts.Token);
        Assert.Equal(("failed", ErrorCodes.Conflict), (again.Status, again.Code));
    }

    [Fact]
    public async Task El_evento_antes_que_la_respuesta_no_se_pierde()
    {
        _obs.EventBeforeResponse = true;
        Start();
        await Until(() => _client.State.State == "ready");
        var r = await _client.ExecuteAsync("0000000000000003", "obs.scene.set", new Dictionary<string, object> { ["scene"] = "Charla" }, _cts.Token);
        Assert.Equal("accepted", r.Status);
        Assert.Equal("completed", (await Finished("0000000000000003")).Status);
        Assert.Equal("Charla", _client.State.Scene);
    }

    [Fact]
    public async Task Escena_o_fuente_inexistente_dan_NOT_FOUND()
    {
        Start();
        await Until(() => _client.State.State == "ready");
        var scene = await _client.ExecuteAsync("0000000000000004", "obs.scene.set", new Dictionary<string, object> { ["scene"] = "No existe" }, _cts.Token);
        Assert.Equal(ErrorCodes.NotFound, scene.Code);
        var input = await _client.ExecuteAsync("0000000000000005", "obs.input.toggleMute", new Dictionary<string, object> { ["input"] = "Cámara" }, _cts.Token);
        Assert.Equal(ErrorCodes.NotFound, input.Code);
    }

    [Fact]
    public async Task Silenciar_fuente_se_confirma_y_actualiza_el_estado()
    {
        Start();
        await Until(() => _client.State.State == "ready");
        await _client.ExecuteAsync("0000000000000006", "obs.input.toggleMute", new Dictionary<string, object> { ["input"] = "Mic/Aux" }, _cts.Token);
        var op = await Finished("0000000000000006");
        Assert.Equal("Mic/Aux silenciado.", op.Message);
        await Until(() => _client.State.Inputs.Single().Muted);
    }

    [Fact]
    public async Task Emision_que_OBS_no_consigue_iniciar_se_informa_como_fallo()
    {
        _obs.FailStartStream = true;
        Start();
        await Until(() => _client.State.State == "ready");
        Assert.Equal("accepted", (await _client.ExecuteAsync("0000000000000007", "obs.streaming.start", NoParams, _cts.Token)).Status);
        var op = await Finished("0000000000000007");
        Assert.Equal("failed", op.Status);
    }

    [Fact]
    public async Task Si_OBS_se_cae_con_una_accion_pendiente_falla_y_se_reconecta()
    {
        _obs.DropEvents = true; // la confirmación nunca llegará
        Start();
        await Until(() => _client.State.State == "ready");
        await _client.ExecuteAsync("0000000000000008", "obs.recording.start", NoParams, _cts.Token);
        _obs.DropEvents = false;
        _obs.DropAll();

        var op = await Finished("0000000000000008");
        Assert.Equal(("failed", ErrorCodes.ObsNotReady), (op.Status, op.Code));
        await Until(() => _client.State.State == "ready", 10_000); // vuelve sola y relee el estado
        Assert.True(_client.State.Recording);
        Assert.True(_obs.Connections >= 2);
    }

    [Fact]
    public async Task Sin_OBS_conectado_las_acciones_fallan_con_OBS_NOT_READY()
    {
        _pid = null;
        Start();
        var r = await _client.ExecuteAsync("0000000000000009", "obs.recording.start", NoParams, _cts.Token);
        Assert.Equal(ErrorCodes.ObsNotReady, r.Code);
    }

    [Fact]
    public async Task Pausar_reanudar_y_detener_con_la_semantica_real_de_OBS()
    {
        // En OBS, el evento PAUSED trae outputActive=false: aun así la grabación sigue en curso.
        Start();
        await Until(() => _client.State.State == "ready");
        await _client.ExecuteAsync("00000000000000a1", "obs.recording.start", NoParams, _cts.Token);
        await Finished("00000000000000a1");

        Assert.Equal("accepted", (await _client.ExecuteAsync("00000000000000a2", "obs.recording.pause", NoParams, _cts.Token)).Status);
        Assert.Equal("Grabación en pausa.", (await Finished("00000000000000a2")).Message);
        await Until(() => _client.State is { Recording: true, RecordingPaused: true });

        Assert.Equal("accepted", (await _client.ExecuteAsync("00000000000000a3", "obs.recording.pause", NoParams, _cts.Token)).Status);
        Assert.Equal("Grabación reanudada.", (await Finished("00000000000000a3")).Message);
        await Until(() => _client.State is { Recording: true, RecordingPaused: false });

        await _client.ExecuteAsync("00000000000000a4", "obs.recording.pause", NoParams, _cts.Token);
        await Finished("00000000000000a4");
        // Detener estando en pausa debe funcionar (antes daba "no hay grabación").
        Assert.Equal("accepted", (await _client.ExecuteAsync("00000000000000a5", "obs.recording.stop", NoParams, _cts.Token)).Status);
        Assert.Equal("completed", (await Finished("00000000000000a5")).Status);
        await Until(() => _client.State is { Recording: false, RecordingPaused: false });
    }

    [Fact]
    public async Task Si_OBS_no_puede_grabar_y_no_avisa_se_informa_del_motivo()
    {
        _obs.SilentStartRecordFailure = true; // carpeta no válida, sin espacio o codificador: solo un aviso en el PC
        Start();
        await Until(() => _client.State.State == "ready");
        Assert.Equal("accepted", (await _client.ExecuteAsync("00000000000000b1", "obs.recording.start", NoParams, _cts.Token)).Status);
        var op = await Finished("00000000000000b1");
        Assert.Equal("failed", op.Status);
        Assert.Contains("revisa el aviso en OBS", op.Message, StringComparison.Ordinal);
        Assert.False(_client.State.Recording);
    }

    [Fact]
    public async Task Si_OBS_no_permite_pausar_se_informa_del_motivo()
    {
        _obs.PauseUnsupported = true; // grabación que comparte codificador con la emisión
        Start();
        await Until(() => _client.State.State == "ready");
        await _client.ExecuteAsync("00000000000000c1", "obs.recording.start", NoParams, _cts.Token);
        await Finished("00000000000000c1");
        await _client.ExecuteAsync("00000000000000c2", "obs.recording.pause", NoParams, _cts.Token);
        var op = await Finished("00000000000000c2");
        Assert.Equal("failed", op.Status);
        Assert.Contains("no permite pausar", op.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cambio_de_coleccion_de_escenas_relee_todo_sin_avalancha_de_peticiones()
    {
        Start();
        await Until(() => _client.State.State == "ready");
        var before = _obs.Requests;
        await _obs.ChangeSceneCollectionAsync(NewScenes, "Intro");
        await Until(() => _client.State.Scene == "Intro" && _client.State.Scenes.SequenceEqual(NewScenes));
        // Una recarga completa (~5 peticiones), no una por cada fuente eliminada.
        Assert.True(_obs.Requests - before <= 12, $"peticiones: {_obs.Requests - before}");
    }

    [Fact]
    public void Cadena_de_autenticacion_del_protocolo()
    {
        // Datos de ejemplo de protocol.md (contraseña, salt y challenge); el resultado está calculado siguiendo sus pasos
        // (la documentación no publica el resultado de estos datos).
        Assert.Equal("1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=",
            ObsClient.AuthString("supersecretpassword", "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=", "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY="));
    }
}
