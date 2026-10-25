using System.Text.Json;
using PegasusDeck.Core.Actions;
using PegasusDeck.Core.Contract;
using PegasusDeck.Core.Security;

namespace PegasusDeck.Tests;

public class ContratoPeticionesTests
{
    public static IEnumerable<object[]> PairValid => Contracts.Examples("pair.request", "valid");
    public static IEnumerable<object[]> PairInvalid => Contracts.Examples("pair.request", "invalid");
    public static IEnumerable<object[]> ActionValid => Contracts.Examples("action.request", "valid");
    public static IEnumerable<object[]> ActionInvalid => Contracts.Examples("action.request", "invalid");
    public static IEnumerable<object[]> ClientValid => Contracts.Examples("events.client", "valid");
    public static IEnumerable<object[]> ClientInvalid => Contracts.Examples("events.client", "invalid");

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Theory, MemberData(nameof(PairValid))]
    public void Emparejamiento_valido_se_acepta(string name, string json) => Assert.True(RequestParser.ParsePair(Parse(json)).Ok, name);

    [Theory, MemberData(nameof(PairInvalid))]
    public void Emparejamiento_invalido_se_rechaza(string name, string json) => Assert.False(RequestParser.ParsePair(Parse(json)).Ok, name);

    [Theory, MemberData(nameof(ActionValid))]
    public void Accion_valida_se_acepta(string name, string json) => Assert.True(RequestParser.ParseAction(Parse(json)).Ok, name);

    [Theory, MemberData(nameof(ActionInvalid))]
    public void Accion_invalida_se_rechaza(string name, string json) => Assert.False(RequestParser.ParseAction(Parse(json)).Ok, name);

    [Theory, MemberData(nameof(ClientValid))]
    public void Auth_valido_da_token(string name, string json) => Assert.True(RequestParser.ParseAuthToken(Parse(json)) is not null, name);

    [Theory, MemberData(nameof(ClientInvalid))]
    public void Auth_invalido_no_da_token(string name, string json) => Assert.True(RequestParser.ParseAuthToken(Parse(json)) is null, name);

    private static readonly string[] Targets =
        ["health", "pair.request", "pair.response", "snapshot", "action.request", "action.response", "events.client", "events.server"];

    public static IEnumerable<object[]> AllExamples =>
        Targets.SelectMany(t => Contracts.Examples(t, "valid").Select(e => new object[] { t, e[0], e[1], true })
                .Concat(Contracts.Examples(t, "invalid").Select(e => new object[] { t, e[0], e[1], false })));

    /// <summary>Control: el validador de esquemas distingue válidos e inválidos (no aprueba todo).</summary>
    [Theory, MemberData(nameof(AllExamples))]
    public void El_validador_de_esquemas_coincide_con_los_ejemplos(string target, string name, string json, bool expected)
    {
        var (valid, errors) = Contracts.Validate(Contracts.SchemaFor(target), json);
        Assert.True(valid == expected, $"{target}/{name}: {errors}");
    }

    [Fact]
    public void Las_respuestas_del_servicio_cumplen_el_contrato()
    {
        var o = JsonDefaults.Options;
        var cases = new (string Schema, object Value)[]
        {
            ("health.schema.json", new HealthResponse(2, new ServiceInfo("pegasus-deck", "0.2.0"))),
            ("pair.schema.json#/$defs/response", new PairSuccess("a1b2c3d4e5f60718", Tokens.NewToken())),
            ("pair.schema.json#/$defs/response", new ErrorResponse(ErrorCodes.PairingInvalid, "x")),
            ("action.schema.json#/$defs/response", new CompletedResponse("ok")),
            ("action.schema.json#/$defs/response", new AcceptedResponse(Tokens.NewOperationId(), null)),
            ("action.schema.json#/$defs/response", new AcceptedResponse(Tokens.NewOperationId(), "Abriendo…")),
            ("events.schema.json#/$defs/server", new OperationMessage(new OperationResult(Tokens.NewOperationId(), "failed", ErrorCodes.Timeout, "x"))),
            ("events.schema.json#/$defs/server", new ErrorMessage(ErrorCodes.AuthRequired, "x")),
        };
        foreach (var (schema, value) in cases)
        {
            var json = JsonSerializer.Serialize(value, value.GetType(), o);
            var (valid, errors) = Contracts.Validate(schema, json);
            Assert.True(valid, $"{json}\n{errors}");
        }
    }
}

public class CatalogoTests
{
    private static ActionRequest Req(string type, params (string, object)[] p) => new(type, p.ToDictionary(x => x.Item1, x => x.Item2));

    [Fact]
    public void Acepta_acciones_registradas_con_parametros_validos()
    {
        Assert.Null(ActionCatalog.Validate(Req("windows.lock")));
        Assert.Null(ActionCatalog.Validate(Req("app.open", ("appId", "obs"))));
        Assert.Null(ActionCatalog.Validate(Req("obs.scene.set", ("scene", "Juego"))));
    }

    [Theory]
    [InlineData("system.exec", ErrorCodes.UnknownAction)]
    [InlineData("local.settings", ErrorCodes.UnknownAction)] // las acciones locales de la PWA no llegan al PC
    public void Rechaza_tipos_no_registrados(string type, string code) => Assert.Equal(code, ActionCatalog.Validate(Req(type))!.Code);

    [Fact]
    public void Rechaza_parametros_peligrosos_o_extra()
    {
        Assert.Equal(ErrorCodes.BadParams, ActionCatalog.Validate(Req("app.open", ("appId", "C:\\Windows\\System32\\cmd.exe")))!.Code);
        Assert.Equal(ErrorCodes.BadParams, ActionCatalog.Validate(Req("app.open", ("appId", "obs"), ("args", "/c calc")))!.Code);
        Assert.Equal(ErrorCodes.BadParams, ActionCatalog.Validate(Req("app.open"))!.Code);
        Assert.Equal(ErrorCodes.BadParams, ActionCatalog.Validate(Req("app.open", ("appId", 3.0)))!.Code);
        Assert.Equal(ErrorCodes.BadParams, ActionCatalog.Validate(Req("obs.scene.set", ("scene", "   ")))!.Code);
    }
}

public class SeguridadTests
{
    [Fact]
    public void Token_de_256_bits_y_solo_hash_en_disco()
    {
        using var dir = new TempDir();
        var store = new DeviceStore(dir.Path);
        var (device, token) = store.Add("iPad");

        Assert.Equal(43, token.Length);
        Assert.Matches("^[a-f0-9]{16}$", device.DeviceId);
        var file = File.ReadAllText(Path.Combine(dir.Path, "devices.json"));
        Assert.DoesNotContain(token, file, StringComparison.Ordinal);
        Assert.Equal(device.DeviceId, store.FindByToken(token)!.DeviceId);
        Assert.Null(store.FindByToken(token + "x"));
        Assert.Null(store.FindByToken(""));
        Assert.Null(store.FindByToken(null));
    }

    [Fact]
    public void Revocar_desde_otro_proceso_se_detecta()
    {
        using var dir = new TempDir();
        var service = new DeviceStore(dir.Path);
        var (device, token) = service.Add("iPad");
        Assert.NotNull(service.FindByToken(token));

        Thread.Sleep(20); // marca de tiempo del archivo distinta
        Assert.True(new DeviceStore(dir.Path).Revoke(device.DeviceId)); // como "devices revoke"
        Assert.Null(service.FindByToken(token));
        Assert.False(service.Exists(device.DeviceId));
    }

    [Fact]
    public void Codigo_de_emparejamiento_de_un_solo_uso()
    {
        using var dir = new TempDir();
        var store = new PairingStore(dir.Path);
        var code = store.Create();
        Assert.Matches("^[0-9]{6}$", code);
        Assert.DoesNotContain(code, File.ReadAllText(Path.Combine(dir.Path, "pairing.json")), StringComparison.Ordinal);
        Assert.Equal(PairingCheck.Ok, store.TryConsume(code));
        Assert.Equal(PairingCheck.Invalid, store.TryConsume(code));
    }

    [Fact]
    public void Codigo_caduca_a_los_2_minutos()
    {
        using var dir = new TempDir();
        var time = new FakeTime(DateTimeOffset.Parse("2026-10-18T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var store = new PairingStore(dir.Path, time);
        var code = store.Create();
        time.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        Assert.Equal(PairingCheck.Invalid, store.TryConsume(code));
    }

    [Fact]
    public void Tras_5_fallos_el_codigo_queda_invalidado()
    {
        using var dir = new TempDir();
        var store = new PairingStore(dir.Path);
        var code = store.Create();
        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 0; i < PairingStore.MaxAttempts; i++) Assert.Equal(PairingCheck.Invalid, store.TryConsume(wrong));
        Assert.Equal(PairingCheck.Invalid, store.TryConsume(code));
    }

    [Fact]
    public void Limite_de_intentos_por_ip_con_ventana_deslizante()
    {
        var time = new FakeTime(DateTimeOffset.UnixEpoch);
        var limiter = new RateLimiter(3, TimeSpan.FromMinutes(1), time);
        Assert.True(limiter.TryAcquire("a"));
        Assert.True(limiter.TryAcquire("a"));
        Assert.True(limiter.TryAcquire("a"));
        Assert.False(limiter.TryAcquire("a"));
        Assert.True(limiter.TryAcquire("b"));
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(limiter.TryAcquire("a"));
    }
}
