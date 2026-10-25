using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PegasusDeck.Agent;
using PegasusDeck.Agent.Audio;
using PegasusDeck.Agent.Media;
using PegasusDeck.Core.Agent;
using PegasusDeck.Core.Apps;
using PegasusDeck.Core.Contract;
using PegasusDeck.Service;
using PegasusDeck.Service.Agent;

namespace PegasusDeck.Tests;

internal sealed class FakeAudio : IAudioController
{
    public bool Muted { get; set; }
    public int Volume { get; set; } = 40;
    public AudioInfo Read() => new("ready", Muted, Volume);
    public void SetMute(bool mute) => Muted = mute;
    public void SetVolume(int percent) => Volume = percent;
    public bool MicMuted { get; set; }
    public MicInfo ReadMic() => new("ready", MicMuted);
    public void SetMicMute(bool mute) => MicMuted = mute;

    /// <summary>Salidas simuladas (ADR-035). Ignores = Windows acepta la orden pero no cambia nada.</summary>
    public List<(string Id, string Name)> Outputs { get; } = [("out-altavoces", "Altavoces"), ("out-cascos", "Auriculares USB")];
    public string DefaultOutput { get; set; } = "out-altavoces";
    public bool Ignores { get; set; }
    public OutputsInfo ReadOutputs() => new("ready", [.. Outputs.Select(o => new OutputDevice(o.Id, o.Name, o.Id == DefaultOutput))]);
    public string? SetDefaultOutput(string id)
    {
        if (!Outputs.Any(o => o.Id == id)) return "Esa salida de audio ya no está conectada.";
        if (!Ignores) DefaultOutput = id;
        return DefaultOutput == id ? null : "Windows no ha cambiado la salida.";
    }
}

/// <summary>Multimedia simulada: la app puede aceptar la orden y no cumplirla (Obeys = false).</summary>
internal sealed class FakeMedia : IMediaController
{
    public string State { get; set; } = "playing";
    public bool Obeys { get; set; } = true;
    public MediaInfo Read() => new(State);
    public Task<bool> PlayAsync()
    {
        if (Obeys) State = "playing";
        return Task.FromResult(true);
    }
    public Task<bool> PauseAsync()
    {
        if (Obeys) State = "paused";
        return Task.FromResult(true);
    }
}

/// <summary>
/// Servicio (PipeAgentLink) y agente (AgentRuntime) reales conectados por un named pipe
/// de pruebas. Como app autorizada se usa el propio agente en modo sin ventana (no
/// abre nada visible ni toca el audio real).
/// </summary>
public sealed class AgentLinkTests : IAsyncLifetime
{
    /// <summary>
    /// App de pruebas: copia del agente con otro nombre (sin ventana). Con el mismo ejecutable
    /// que un agente real, este contaría como "la app ya abierta".
    /// </summary>
    private static readonly string AgentExe = CreateTestApp();

    private static string CreateTestApp()
    {
        var target = Path.Combine(AppContext.BaseDirectory, "PegasusDeck.TestApp.exe");
        if (!File.Exists(target)) File.Copy(Path.Combine(AppContext.BaseDirectory, "PegasusDeck.Agent.exe"), target);
        return target;
    }
    private readonly TempDir _data = new();
    private readonly string _pipe = $"PegasusDeck.Test.{Guid.NewGuid():N}";
    private readonly string _dummyPipe = $"PegasusDeck.Dummy.{Guid.NewGuid():N}";
    private readonly CancellationTokenSource _service = new();
    private readonly CancellationTokenSource _agent = new();
    private readonly FakeAudio _audio = new();
    private readonly FakeMedia _media = new();
    private readonly ServiceEvents _events = new();
    private OperationTracker _tracker = null!;
    private PipeAgentLink _link = null!;
    private AgentRuntime _runtime = null!;
    private Task _agentRun = Task.CompletedTask;
    private readonly DateTime _started = DateTime.Now;

    public async Task InitializeAsync()
    {
        // Lista blanca: el agente sin ventana como app "dummy" (process) y como "dummy-win" (window: nunca lista).
        var args = new[] { "--headless", $"--pipe={_dummyPipe}" };
        var apps = new[]
        {
            // La copia de pruebas está en bin (carpeta del usuario): se permite expresamente.
            new AppEntry("dummy", "Dummy", AgentExe, args, "process", false, AllowUnprotectedPath: true),
            new AppEntry("dummy-win", "Dummy con ventana", AgentExe, [.. args.Select(a => a.Replace(_dummyPipe, _dummyPipe + "-w", StringComparison.Ordinal))], "window", false, AllowUnprotectedPath: true),
            // Con una ventana de verdad (fuera de la pantalla) para probar el cierre de ventanas.
            new AppEntry("dummy-gui", "Dummy con ventana real", AgentExe, ["--test-window"], "window", false, AllowUnprotectedPath: true),
            new AppEntry("missing", "No instalada", @"C:\NoExiste\nada.exe", [], "process", false),
            new AppEntry("unprotected", "Sin permiso expreso", AgentExe, args, "process", false),
        };
        File.WriteAllText(Path.Combine(_data.Path, AppsFile.FileName), JsonSerializer.Serialize(apps, JsonDefaults.Options));

        var options = Options.Create(new PegasusOptions
        {
            DataDir = _data.Path,
            AgentPipeName = _pipe,
            TrustedAgentPath = "*",
            UnprotectedDataDir = true,
            DevelopmentHttp = true,
            AllowedHosts = ["localhost"],
        });
        _tracker = new OperationTracker(_events, TimeProvider.System);
        _link = new PipeAgentLink(options, _tracker, _events, NullLogger<PipeAgentLink>.Instance) { AppsPoll = TimeSpan.FromMilliseconds(200) };
        await _link.StartAsync(_service.Token);
        StartAgent();
        await Until(() => _link.Current.SessionReady);
    }

    private void StartAgent()
    {
        var agentOptions = new AgentOptions { PipeName = _pipe, TrustedServicePath = "*", InsecureDev = true };
        _runtime = new AgentRuntime(agentOptions, _audio, obsProcessId: () => null, media: _media) { AppsPoll = TimeSpan.FromMilliseconds(200) };
        _agentRun = _runtime.RunAsync(_agent.Token);
    }

    public async Task DisposeAsync()
    {
        await _agent.CancelAsync();
        try { await _agentRun; } catch (OperationCanceledException) { }
        await _service.CancelAsync();
        await _link.StopAsync(CancellationToken.None);
        _link.Dispose();
        _runtime.Dispose();
        _tracker.Dispose();
        // Cierra las instancias "dummy" que hayan abierto las pruebas.
        foreach (var p in Process.GetProcessesByName("PegasusDeck.TestApp"))
        {
            using (p)
            {
                try { if (p.StartTime >= _started.AddSeconds(-1)) p.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            }
        }
        _service.Dispose();
        _agent.Dispose();
        _data.Dispose();
    }

    private static async Task Until(Func<bool> condition, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("La condición no se cumplió a tiempo.");
            await Task.Delay(25);
        }
    }

    private async Task<OperationResult> NextOperation(string id, int timeoutMs = 15_000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        while (true)
        {
            var item = await _events.Reader.ReadAsync(cts.Token);
            if (item is OperationMessage m && m.Data.OperationId == id) return m.Data;
        }
    }

    private ActionDispatcher Dispatcher() => new(_link, _tracker, new Confirmations(TimeProvider.System), NullLogger<ActionDispatcher>.Instance);

    private static ActionRequest Req(string type, params (string K, object V)[] p) => new(type, p.ToDictionary(x => x.K, x => x.V));

    [Fact]
    public void El_agente_informa_de_sesion_apps_y_audio()
    {
        var s = _link.Current;
        Assert.True(s.SessionReady);
        Assert.Equal(AppsDetection.Ok, s.AppsDetection);
        Assert.Equal("installed", s.Apps.Single(a => a.Id == "dummy").State);
        Assert.Equal("not_installed", s.Apps.Single(a => a.Id == "missing").State);
        Assert.Equal(new AudioInfo("ready", false, 40), s.Audio);
    }

    [Fact]
    public async Task Audio_se_ejecuta_en_el_agente_y_el_estado_vuelve_al_servicio()
    {
        var r = await Dispatcher().DispatchAsync(Req("audio.master.setVolume", ("percent", 70.0)), "test", default);
        Assert.Equal(new ActionOutcome.Completed("Volumen: 70 %."), r);
        Assert.Equal(70, _audio.Volume);
        await Until(() => _link.Current.Audio.Volume == 70);

        var mute = await Dispatcher().DispatchAsync(Req("audio.master.toggleMute"), "test", default);
        Assert.IsType<ActionOutcome.Completed>(mute);
        await Until(() => _link.Current.Audio.Muted);
    }

    [Fact]
    public async Task Al_cambiar_apps_json_el_agente_y_el_ipad_ven_la_lista_nueva_sin_reiniciar()
    {
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        var added = AppsEditor.Add(_data.Path, notepad, "Bloc de notas", allowUnprotectedPath: false);
        Assert.True(added.Ok, added.Message);
        await Until(() => _runtime.Apps.Find(added.Id!) is not null);
        await Until(() => _link.Current.Apps.Any(a => a.Id == added.Id));

        Assert.True(AppsEditor.Remove(_data.Path, "dummy-win").Ok);
        await Until(() => _runtime.Apps.Find("dummy-win") is null && _link.Current.Apps.All(a => a.Id != "dummy-win"));
        var r = await Dispatcher().DispatchAsync(Req("app.open", ("appId", "dummy-win")), "test", default);
        Assert.Equal(ErrorCodes.AppNotAllowed, Assert.IsType<ActionOutcome.Failed>(r).Code);
    }

    [Fact]
    public async Task Cerrar_todas_las_ventanas_de_una_app_con_dos_pasos_y_sin_forzar()
    {
        // App de pruebas con ventana (sin tocar las apps reales del usuario).
        var open = await Dispatcher().DispatchAsync(Req("app.open", ("appId", "dummy-gui")), "test", default);
        var accepted = Assert.IsType<ActionOutcome.Accepted>(open);
        Assert.Equal("completed", (await NextOperation(accepted.OperationId)).Status);
        await Until(() => _link.Current.Apps.Single(a => a.Id == "dummy-gui").Windows >= 1);

        var dispatcher = Dispatcher();
        var first = Assert.IsType<ActionOutcome.ConfirmRequired>(await dispatcher.DispatchAsync(Req("app.closeAll", ("appId", "dummy-gui")), "test", default));
        Assert.Equal("ready", _link.Current.Apps.Single(a => a.Id == "dummy-gui").State); // el primer paso no cierra nada

        var request = new ActionRequest("app.closeAll", new Dictionary<string, object> { ["appId"] = "dummy-gui" }, first.Token);
        var done = Assert.IsType<ActionOutcome.Completed>(await dispatcher.DispatchAsync(request, "test", default));
        Assert.Contains("Cerrada 1 ventana", done.Message, StringComparison.Ordinal);
        await Until(() => _link.Current.Apps.Single(a => a.Id == "dummy-gui").Windows == 0);

        // Ya no hay ventanas: se dice, no se finge.
        var again = Assert.IsType<ActionOutcome.ConfirmRequired>(await dispatcher.DispatchAsync(Req("app.closeAll", ("appId", "dummy-gui")), "test", default));
        var none = await dispatcher.DispatchAsync(request with { ConfirmToken = again.Token }, "test", default);
        Assert.Equal(ErrorCodes.Conflict, Assert.IsType<ActionOutcome.Failed>(none).Code);
    }

    [Fact]
    public async Task Encender_y_apagar_el_PC_ya_no_existen()
    {
        // ADR-032: ni con confirmación; el servicio no tiene a quién pedírselo.
        foreach (var type in new[] { "system.shutdown", "system.restart", "system.wake" })
        {
            var r = await Dispatcher().DispatchAsync(Req(type), "test", default);
            Assert.Equal(ErrorCodes.UnknownAction, Assert.IsType<ActionOutcome.Failed>(r).Code);
        }
    }

    [Fact]
    public async Task Ordenes_de_Resolve_sin_Resolve_autorizado_no_pulsan_nada()
    {
        var r = await Dispatcher().DispatchAsync(Req("davinci.command", ("command", "project.save")), "test", default);
        Assert.Equal(ErrorCodes.AppNotAllowed, Assert.IsType<ActionOutcome.Failed>(r).Code);
        var bad = await Dispatcher().DispatchAsync(Req("davinci.command", ("command", "Alt+F4")), "test", default);
        Assert.Equal(ErrorCodes.BadParams, Assert.IsType<ActionOutcome.Failed>(bad).Code);
    }

    [Fact]
    public async Task Microfono_se_silencia_y_se_confirma_leyendo_el_estado()
    {
        Assert.Equal(new MicInfo("ready", false), _link.Current.Mic);
        var r = await Dispatcher().DispatchAsync(Req("audio.mic.toggleMute"), "test", default);
        Assert.Equal(new ActionOutcome.Completed("Micrófono silenciado."), r);
        Assert.True(_audio.MicMuted);
        await Until(() => _link.Current.Mic?.Muted == true);

        var again = await Dispatcher().DispatchAsync(Req("audio.mic.toggleMute"), "test", default);
        Assert.Equal(new ActionOutcome.Completed("Micrófono activado."), again);
    }

    [Fact]
    public async Task Salida_de_audio_cambia_solo_a_una_conectada_y_se_confirma_leyendo_Windows()
    {
        await Until(() => _link.Current.Outputs?.Devices.Count == 2);
        Assert.True(_link.Current.Outputs!.Devices.Single(d => d.Id == "out-altavoces").Default);

        var r = await Dispatcher().DispatchAsync(Req("audio.output.set", ("device", "out-cascos")), "test", default);
        Assert.Equal(new ActionOutcome.Completed("Ahora suena por Auriculares USB."), r);
        Assert.Equal("out-cascos", _audio.DefaultOutput);
        await Until(() => _link.Current.Outputs?.Devices.Single(d => d.Id == "out-cascos").Default == true);

        // Una salida que no existe: no se cambia nada.
        var gone = await Dispatcher().DispatchAsync(Req("audio.output.set", ("device", "out-hdmi")), "test", default);
        Assert.Equal(ErrorCodes.NotFound, Assert.IsType<ActionOutcome.Failed>(gone).Code);
        Assert.Equal("out-cascos", _audio.DefaultOutput);

        // Windows acepta pero no cambia: se dice, no se finge.
        _audio.Ignores = true;
        var ignored = await Dispatcher().DispatchAsync(Req("audio.output.set", ("device", "out-altavoces")), "test", default);
        Assert.Contains("no ha cambiado", Assert.IsType<ActionOutcome.Failed>(ignored).Message, StringComparison.Ordinal);

        // Solo ids: nunca el identificador de Windows ni texto libre.
        var bad = await Dispatcher().DispatchAsync(Req("audio.output.set", ("device", "{0.0.0.00000000}.{abc}")), "test", default);
        Assert.Equal(ErrorCodes.BadParams, Assert.IsType<ActionOutcome.Failed>(bad).Code);
    }

    [Fact]
    public async Task Multimedia_pausa_y_reanuda_solo_si_el_estado_lo_permite()
    {
        await Until(() => _link.Current.Media?.State == "playing");
        var play = await Dispatcher().DispatchAsync(Req("media.play"), "test", default);
        Assert.Equal(ErrorCodes.Conflict, Assert.IsType<ActionOutcome.Failed>(play).Code); // ya suena

        var pause = await Dispatcher().DispatchAsync(Req("media.pause"), "test", default);
        Assert.Equal(new ActionOutcome.Completed("Reproducción en pausa."), pause);
        await Until(() => _link.Current.Media?.State == "paused");

        var resume = await Dispatcher().DispatchAsync(Req("media.play"), "test", default);
        Assert.Equal(new ActionOutcome.Completed("Reproducción reanudada."), resume);
    }

    [Fact]
    public async Task Multimedia_no_afirma_la_pausa_si_la_app_no_la_cumple()
    {
        _media.Obeys = false;
        var r = await Dispatcher().DispatchAsync(Req("media.pause"), "test", default);
        Assert.Equal(ErrorCodes.Timeout, Assert.IsType<ActionOutcome.Failed>(r).Code);

        _media.State = "none";
        await Until(() => _link.Current.Media?.State == "none");
        var nothing = await Dispatcher().DispatchAsync(Req("media.pause"), "test", default);
        Assert.Equal("No hay nada reproduciéndose en el PC.", Assert.IsType<ActionOutcome.Failed>(nothing).Message);
    }

    [Fact]
    public async Task Abrir_app_se_acepta_y_se_confirma_cuando_esta_lista()
    {
        var r = await Dispatcher().DispatchAsync(Req("app.open", ("appId", "dummy")), "test", default);
        var accepted = Assert.IsType<ActionOutcome.Accepted>(r);
        var op = await NextOperation(accepted.OperationId);
        Assert.Equal("completed", op.Status);
        await Until(() => _link.Current.Apps.Single(a => a.Id == "dummy").State == "ready");

        // Ya abierta: no se lanza otra; se intenta traer al frente y se responde al momento.
        var again = await Dispatcher().DispatchAsync(Req("app.open", ("appId", "dummy")), "test", default);
        Assert.IsType<ActionOutcome.Completed>(again);
    }

    [Fact]
    public async Task Apps_fuera_de_la_lista_blanca_o_no_instaladas_se_rechazan()
    {
        var notAllowed = await Dispatcher().DispatchAsync(Req("app.open", ("appId", "calc")), "test", default);
        Assert.Equal(ErrorCodes.AppNotAllowed, Assert.IsType<ActionOutcome.Failed>(notAllowed).Code);
        var missing = await Dispatcher().DispatchAsync(Req("app.open", ("appId", "missing")), "test", default);
        Assert.Equal(ErrorCodes.AppNotFound, Assert.IsType<ActionOutcome.Failed>(missing).Code);
        Assert.Equal(0, _tracker.Pending);
    }

    [Fact]
    public async Task Ejecutable_modificable_por_usuarios_sin_permiso_expreso_no_se_lanza()
    {
        var r = await Dispatcher().DispatchAsync(Req("app.open", ("appId", "unprotected")), "test", default);
        var failed = Assert.IsType<ActionOutcome.Failed>(r);
        Assert.Equal(ErrorCodes.AppNotAllowed, failed.Code);
        Assert.Contains("carpeta protegida", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_estado_mal_formado_del_agente_corta_la_conexion_sin_romper_la_instantanea()
    {
        var pipe = $"PegasusDeck.Test.{Guid.NewGuid():N}";
        var options = Options.Create(new PegasusOptions { DataDir = _data.Path, AgentPipeName = pipe, TrustedAgentPath = "*", UnprotectedDataDir = true, DevelopmentHttp = true, AllowedHosts = ["localhost"] });
        var events = new ServiceEvents();
        using var tracker = new OperationTracker(events, TimeProvider.System);
        using var server = new PipeAgentLink(options, tracker, events, NullLogger<PipeAgentLink>.Instance);
        using var stop = new CancellationTokenSource();
        await server.StartAsync(stop.Token);

        await using var client = new System.IO.Pipes.NamedPipeClientStream(".", pipe, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var channel = new LineChannel(client);
        await channel.SendAsync(new AgentHello(AgentProtocol.Version, "falso", 1), default);
        using (await channel.ReceiveAsync(default)) { } // hello del servicio
        // Estado con nulos (como enviaría un agente manipulado).
        await client.WriteAsync("{\"type\":\"state\",\"sessionReady\":true,\"obs\":{\"state\":\"ready\"}}\n"u8.ToArray());
        await client.FlushAsync();

        await Task.Delay(500);
        Assert.False(server.Current.SessionReady);
        var state = new SystemState(server).Build(); // no lanza
        Assert.Equal("unavailable", state.Obs.State);
        await stop.CancelAsync();
        await server.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task El_servicio_acepta_al_agente_por_su_ruta_real_en_la_sesion_activa()
    {
        // Sin "*": el servicio exige la ruta del ejecutable del cliente (aquí, este proceso) y la sesión de la consola.
        var pipe = $"PegasusDeck.Test.{Guid.NewGuid():N}";
        var self = Environment.ProcessPath!;
        var options = Options.Create(new PegasusOptions { DataDir = _data.Path, AgentPipeName = pipe, TrustedAgentPath = self, UnprotectedDataDir = true, DevelopmentHttp = true, AllowedHosts = ["localhost"] });
        using var tracker = new OperationTracker(new ServiceEvents(), TimeProvider.System);
        using var server = new PipeAgentLink(options, tracker, new ServiceEvents(), NullLogger<PipeAgentLink>.Instance);
        using var stop = new CancellationTokenSource();
        await server.StartAsync(stop.Token);
        using var agent = new AgentRuntime(new AgentOptions { PipeName = pipe, TrustedServicePath = "*", InsecureDev = true }, new FakeAudio(), () => null);
        var run = agent.RunAsync(stop.Token);
        await Until(() => server.Current.SessionReady);
        await stop.CancelAsync();
        try { await run; } catch (OperationCanceledException) { }
        await server.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task El_agente_no_acepta_un_servidor_fuera_de_la_sesion_0()
    {
        // La ruta coincide (este proceso), pero no es un servicio de Windows: se rechaza.
        var pipe = $"PegasusDeck.Test.{Guid.NewGuid():N}";
        var options = Options.Create(new PegasusOptions { DataDir = _data.Path, AgentPipeName = pipe, TrustedAgentPath = "*", UnprotectedDataDir = true, DevelopmentHttp = true, AllowedHosts = ["localhost"] });
        using var tracker = new OperationTracker(new ServiceEvents(), TimeProvider.System);
        using var server = new PipeAgentLink(options, tracker, new ServiceEvents(), NullLogger<PipeAgentLink>.Instance);
        using var stop = new CancellationTokenSource();
        await server.StartAsync(stop.Token);
        var statuses = new List<string>();
        using var agent = new AgentRuntime(new AgentOptions { PipeName = pipe, TrustedServicePath = Environment.ProcessPath! }, new FakeAudio(), () => null);
        agent.Connection.StatusChanged += s => { lock (statuses) statuses.Add(s); };
        var run = agent.RunAsync(stop.Token);
        await Until(() => { lock (statuses) return statuses.Any(s => s.Contains("sesión 0", StringComparison.Ordinal)); });
        Assert.False(server.Current.SessionReady);
        await stop.CancelAsync();
        try { await run; } catch (OperationCanceledException) { }
        await server.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Sin_OBS_las_acciones_de_OBS_fallan_al_momento()
    {
        var r = await Dispatcher().DispatchAsync(Req("obs.recording.start"), "test", default);
        Assert.Equal(ErrorCodes.ObsNotReady, Assert.IsType<ActionOutcome.Failed>(r).Code);
    }

    [Fact]
    public async Task Si_el_agente_se_desconecta_las_operaciones_pendientes_fallan()
    {
        // "dummy-win" exige ventana: nunca estará lista, la operación queda pendiente.
        var r = await Dispatcher().DispatchAsync(Req("app.open", ("appId", "dummy-win")), "test", default);
        var accepted = Assert.IsType<ActionOutcome.Accepted>(r);
        Assert.Equal(1, _tracker.Pending);

        await _agent.CancelAsync();
        var op = await NextOperation(accepted.OperationId);
        Assert.Equal(("failed", ErrorCodes.NoSession), (op.Status, op.Code));
        await Until(() => !_link.Current.SessionReady);

        var after = await Dispatcher().DispatchAsync(Req("windows.lock"), "test", default);
        Assert.Equal(ErrorCodes.NoSession, Assert.IsType<ActionOutcome.Failed>(after).Code);
    }

    [Fact]
    public async Task Un_cliente_que_no_es_el_agente_de_confianza_se_rechaza()
    {
        var pipe = $"PegasusDeck.Test.{Guid.NewGuid():N}";
        var options = Options.Create(new PegasusOptions
        {
            DataDir = _data.Path, AgentPipeName = pipe, TrustedAgentPath = @"C:\Program Files\PegasusDeck\PegasusDeck.Agent.exe",
            UnprotectedDataDir = true, DevelopmentHttp = true, AllowedHosts = ["localhost"],
        });
        using var tracker = new OperationTracker(new ServiceEvents(), TimeProvider.System);
        using var strict = new PipeAgentLink(options, tracker, new ServiceEvents(), NullLogger<PipeAgentLink>.Instance);
        using var stop = new CancellationTokenSource();
        await strict.StartAsync(stop.Token);

        using var intruder = new AgentRuntime(new AgentOptions { PipeName = pipe, TrustedServicePath = "*", InsecureDev = true }, new FakeAudio(), () => null);
        var run = intruder.RunAsync(stop.Token);
        await Task.Delay(2000);
        Assert.False(strict.Current.SessionReady);
        Assert.False(intruder.Connection.Connected);

        await stop.CancelAsync();
        try { await run; } catch (OperationCanceledException) { }
        await strict.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task El_agente_no_confia_en_un_servidor_que_no_es_el_servicio()
    {
        // El pipe lo sirve este proceso (testhost), no PegasusDeck.Service.exe.
        using var stop = new CancellationTokenSource();
        var statuses = new List<string>();
        using var careful = new AgentRuntime(new AgentOptions { PipeName = _pipe, TrustedServicePath = @"C:\Program Files\PegasusDeck\PegasusDeck.Service.exe" }, new FakeAudio(), () => null);
        careful.Connection.StatusChanged += s => { lock (statuses) statuses.Add(s); };
        // El servicio de pruebas ya tiene un agente conectado; se usa un pipe propio sin agente.
        var pipe = $"PegasusDeck.Test.{Guid.NewGuid():N}";
        var options = Options.Create(new PegasusOptions { DataDir = _data.Path, AgentPipeName = pipe, TrustedAgentPath = "*", UnprotectedDataDir = true, DevelopmentHttp = true, AllowedHosts = ["localhost"] });
        using var tracker = new OperationTracker(new ServiceEvents(), TimeProvider.System);
        using var server = new PipeAgentLink(options, tracker, new ServiceEvents(), NullLogger<PipeAgentLink>.Instance);
        await server.StartAsync(stop.Token);
        using var careful2 = new AgentRuntime(new AgentOptions { PipeName = pipe, TrustedServicePath = @"C:\Program Files\PegasusDeck\PegasusDeck.Service.exe" }, new FakeAudio(), () => null);
        careful2.Connection.StatusChanged += s => { lock (statuses) statuses.Add(s); };
        var run = careful2.RunAsync(stop.Token);

        await Until(() => { lock (statuses) return statuses.Any(s => s.Contains("no es el servicio", StringComparison.Ordinal)); });
        Assert.False(server.Current.SessionReady);

        await stop.CancelAsync();
        try { await run; } catch (OperationCanceledException) { }
        await server.StopAsync(CancellationToken.None);
    }
}
