using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PegasusDeck.Core.Security;

namespace PegasusDeck.Tests;

/// <summary>Servicio real en memoria (TestServer) con datos y PWA en carpetas temporales.</summary>
public sealed class ServiceFixture : WebApplicationFactory<Program>
{
    public TempDir Data { get; } = new();
    public TempDir Web { get; } = new();
    public const string Origin = "http://localhost:8443";
    public string PipeName { get; } = $"PegasusDeck.Test.{Guid.NewGuid():N}";

    public ServiceFixture()
    {
        File.WriteAllText(Path.Combine(Web.Path, "index.html"), "<!doctype html><title>Pegasus Deck</title>");
        File.WriteAllText(Path.Combine(Web.Path, "sw.js"), "// sw");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Pegasus:DataDir", Data.Path);
        builder.UseSetting("Pegasus:WebRoot", Web.Path);
        builder.UseSetting("Pegasus:DevelopmentHttp", "true");
        builder.UseSetting("Pegasus:Port", "8443");
        builder.UseSetting("Pegasus:AllowedHosts:0", "localhost");
        builder.UseSetting("Pegasus:HeartbeatSeconds", "1");
        builder.UseSetting("Pegasus:UnprotectedDataDir", "true"); // carpeta temporal del usuario de pruebas
        // En TestServer todas las peticiones llegan sin IP (misma clave): límite alto salvo en su prueba.
        builder.UseSetting("Pegasus:PairAttemptsPerMinute", "40");
        // Pipe propio por servidor de pruebas: no choca con otras pruebas ni con un servicio real.
        builder.UseSetting("Pegasus:AgentPipeName", PipeName);
        builder.UseSetting("Pegasus:TrustedAgentPath", "*");
    }

    public DeviceStore Devices => Services.GetRequiredService<DeviceStore>();
    public PairingStore Pairing => Services.GetRequiredService<PairingStore>();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Data.Dispose();
            Web.Dispose();
        }
    }
}

public class ServicioTests(ServiceFixture app) : IClassFixture<ServiceFixture>
{
    private static async Task<(HttpStatusCode Status, string Json)> Send(HttpClient c, HttpMethod m, string url, object? body = null, string? token = null, string? origin = null)
    {
        using var req = new HttpRequestMessage(m, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (token is not null) req.Headers.Authorization = new("Bearer", token);
        if (origin is not null) req.Headers.Add("Origin", origin);
        using var res = await c.SendAsync(req);
        return (res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    private static void AssertSchema(string schema, string json)
    {
        var (valid, errors) = Contracts.Validate(schema, json);
        Assert.True(valid, $"{json}\n{errors}");
    }

    [Fact]
    public async Task Health_solo_devuelve_protocolo_y_version()
    {
        var (status, json) = await Send(app.CreateClient(), HttpMethod.Get, "/api/health");
        Assert.Equal(HttpStatusCode.OK, status);
        AssertSchema("health.schema.json", json);
        Assert.Contains("\"protocol\":2", json, StringComparison.Ordinal);
        // Sin datos del sistema: solo protocolo y servicio (nombre y versión).
        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal(["protocol", "service"], root.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(["name", "version"], root.GetProperty("service").EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task Emparejamiento_completo_y_codigo_de_un_solo_uso()
    {
        var c = app.CreateClient();
        var bad = await Send(c, HttpMethod.Post, "/api/pair", new { code = "123456", deviceName = "iPad" }, origin: ServiceFixture.Origin);
        Assert.Equal(HttpStatusCode.Unauthorized, bad.Status);
        AssertSchema("pair.schema.json#/$defs/response", bad.Json);

        var code = app.Pairing.Create();
        var ok = await Send(c, HttpMethod.Post, "/api/pair", new { code, deviceName = "iPad" }, origin: ServiceFixture.Origin);
        Assert.Equal(HttpStatusCode.OK, ok.Status);
        AssertSchema("pair.schema.json#/$defs/response", ok.Json);
        var token = JsonDocument.Parse(ok.Json).RootElement.GetProperty("token").GetString()!;
        Assert.NotNull(app.Devices.FindByToken(token));

        var reuse = await Send(c, HttpMethod.Post, "/api/pair", new { code, deviceName = "iPad" });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.Status);
    }

    [Fact]
    public async Task Peticiones_mal_formadas_se_rechazan()
    {
        var c = app.CreateClient();
        using var junk = new StringContent("{no es json", Encoding.UTF8, "application/json");
        var r1 = await c.PostAsync(new Uri("/api/pair", UriKind.Relative), junk);
        Assert.Equal(HttpStatusCode.BadRequest, r1.StatusCode);

        var r2 = await Send(c, HttpMethod.Post, "/api/pair", new { code = "12", deviceName = "iPad" });
        Assert.Equal(HttpStatusCode.BadRequest, r2.Status);
        AssertSchema("pair.schema.json#/$defs/response", r2.Json);

        using var huge = new StringContent(new string('x', 20_000), Encoding.UTF8, "application/json");
        var r3 = await c.PostAsync(new Uri("/api/pair", UriKind.Relative), huge);
        Assert.Equal(HttpStatusCode.BadRequest, r3.StatusCode);
    }

    [Fact]
    public async Task Acciones_requieren_token_y_lista_blanca()
    {
        var c = app.CreateClient();
        var (_, token) = app.Devices.Add("pruebas");

        var noToken = await Send(c, HttpMethod.Post, "/api/actions", new { type = "windows.lock", @params = new { } });
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.Status);
        AssertSchema("action.schema.json#/$defs/response", noToken.Json);

        var badToken = await Send(c, HttpMethod.Post, "/api/actions", new { type = "windows.lock", @params = new { } }, token: "x" + token);
        Assert.Equal(HttpStatusCode.Unauthorized, badToken.Status);

        var cases = new (object Body, HttpStatusCode Status, string Code)[]
        {
            (new { type = "system.exec", @params = new { cmd = "calc" } }, HttpStatusCode.BadRequest, "UNKNOWN_ACTION"),
            (new { type = "app.open", @params = new { appId = "C:/Windows/cmd.exe" } }, HttpStatusCode.BadRequest, "BAD_PARAMS"),
            (new { type = "app.open", @params = new { appId = "obs", args = "/c calc" } }, HttpStatusCode.BadRequest, "BAD_PARAMS"),
            (new { type = "app.open", @params = new { appId = new { path = "x" } } }, HttpStatusCode.BadRequest, "BAD_PARAMS"),
            // Válida, pero en la fase 2 no hay agente de sesión: el servicio lo dice, no finge.
            (new { type = "windows.lock", @params = new { } }, HttpStatusCode.Conflict, "NO_SESSION"),
        };
        foreach (var (body, status, code) in cases)
        {
            var r = await Send(c, HttpMethod.Post, "/api/actions", body, token: token);
            Assert.Equal(status, r.Status);
            Assert.Contains($"\"code\":\"{code}\"", r.Json, StringComparison.Ordinal);
            AssertSchema("action.schema.json#/$defs/response", r.Json);
        }
    }

    [Fact]
    public async Task Destructivas_exigen_dos_pasos_con_token_de_un_solo_uso_del_mismo_dispositivo()
    {
        var c = app.CreateClient();
        var (_, token) = app.Devices.Add("pruebas");
        var (_, other) = app.Devices.Add("otro iPad");
        var close = new { type = "app.closeAll", @params = new { appId = "notepad" } };
        object Step(string confirm) => new { type = "app.closeAll", @params = new { appId = "notepad" }, confirmToken = confirm };

        // Primer paso: no ejecuta nada; devuelve el token.
        var first = await Send(c, HttpMethod.Post, "/api/actions", close, token: token);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        AssertSchema("action.schema.json#/$defs/response", first.Json);
        var confirm = JsonDocument.Parse(first.Json).RootElement.GetProperty("confirmToken").GetString()!;

        // Otro dispositivo no puede usar ese token (y lo gasta: un solo uso).
        var stolen = await Send(c, HttpMethod.Post, "/api/actions", Step(confirm), token: other);
        Assert.Contains("CONFIRMATION_INVALID", stolen.Json, StringComparison.Ordinal);

        // Nuevo primer paso + segundo paso correcto: pasa la confirmación y llega al agente (aquí no hay sesión).
        var again = await Send(c, HttpMethod.Post, "/api/actions", close, token: token);
        var t2 = JsonDocument.Parse(again.Json).RootElement.GetProperty("confirmToken").GetString()!;
        var done = await Send(c, HttpMethod.Post, "/api/actions", Step(t2), token: token);
        Assert.DoesNotContain("CONFIRMATION_INVALID", done.Json, StringComparison.Ordinal);
        Assert.Contains("NO_SESSION", done.Json, StringComparison.Ordinal);

        // Repetir el mismo token no vale.
        var replay = await Send(c, HttpMethod.Post, "/api/actions", Step(t2), token: token);
        Assert.Contains("CONFIRMATION_INVALID", replay.Json, StringComparison.Ordinal);

        // Un token con formato no válido se rechaza al leer la petición.
        var bad = await Send(c, HttpMethod.Post, "/api/actions", Step("<script>"), token: token);
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
    }

    [Fact]
    public async Task Apagar_el_PC_ya_no_es_una_accion()
    {
        var c = app.CreateClient();
        var (_, token) = app.Devices.Add("pruebas");
        var r = await Send(c, HttpMethod.Post, "/api/actions", new { type = "system.shutdown", @params = new { } }, token: token);
        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Contains("UNKNOWN_ACTION", r.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Origen_y_host_no_autorizados_se_rechazan()
    {
        var c = app.CreateClient();
        var evil = await Send(c, HttpMethod.Get, "/api/health", origin: "http://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, evil.Status);
        Assert.Contains("ORIGIN_FORBIDDEN", evil.Json, StringComparison.Ordinal);

        var good = await Send(c, HttpMethod.Get, "/api/health", origin: ServiceFixture.Origin);
        Assert.Equal(HttpStatusCode.OK, good.Status);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        req.Headers.Host = "attacker.example"; // DNS rebinding
        using var res = await c.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Contains("HOST_FORBIDDEN", await res.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sirve_la_PWA_y_las_rutas_api_desconocidas_dan_404_json()
    {
        var c = app.CreateClient();
        var page = await c.GetAsync(new Uri("/cualquier/ruta", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Pegasus Deck", await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var sw = await c.GetAsync(new Uri("/sw.js", UriKind.Relative));
        Assert.Equal("no-cache", sw.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", sw.Headers.GetValues("X-Content-Type-Options").Single());

        var api = await Send(c, HttpMethod.Get, "/api/no-existe");
        Assert.Equal(HttpStatusCode.NotFound, api.Status);
        Assert.Contains("NOT_FOUND", api.Json, StringComparison.Ordinal);
    }

    private async Task<WebSocket> ConnectEvents()
    {
        var client = app.Server.CreateWebSocketClient();
        client.ConfigureRequest = r => r.Headers.Origin = ServiceFixture.Origin;
        return await client.ConnectAsync(new Uri("ws://localhost/api/events"), CancellationToken.None);
    }

    private static async Task<(string? Text, WebSocketCloseStatus? Close)> Receive(WebSocket ws)
    {
        var buffer = new byte[64 * 1024];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var total = 0;
        while (true)
        {
            var r = await ws.ReceiveAsync(buffer.AsMemory(total), cts.Token);
            if (r.MessageType == WebSocketMessageType.Close) return (null, ws.CloseStatus);
            total += r.Count;
            if (r.EndOfMessage) return (Encoding.UTF8.GetString(buffer, 0, total), null);
        }
    }

    private static Task SendText(WebSocket ws, string text) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

    [Fact]
    public async Task WebSocket_con_token_invalido_se_cierra_con_4401()
    {
        using var ws = await ConnectEvents();
        await SendText(ws, """{"type":"auth","token":"no-vale"}""");
        var (msg, _) = await Receive(ws);
        AssertSchema("events.schema.json#/$defs/server", msg!);
        Assert.Contains("AUTH_REQUIRED", msg!, StringComparison.Ordinal);
        var (_, close) = await Receive(ws);
        Assert.Equal(4401, (int)close!.Value);
    }

    [Fact]
    public async Task WebSocket_autenticado_recibe_ready_latido_y_se_cierra_al_revocar()
    {
        var (device, token) = app.Devices.Add("pruebas");
        using var ws = await ConnectEvents();
        await SendText(ws, JsonSerializer.Serialize(new { type = "auth", token }));

        var (ready, _) = await Receive(ws);
        AssertSchema("events.schema.json#/$defs/server", ready!);
        Assert.StartsWith("{\"type\":\"ready\"", ready!, StringComparison.Ordinal);
        Assert.Contains("\"session\":{\"ready\":false}", ready!, StringComparison.Ordinal);

        var (beat, _) = await Receive(ws); // latido cada 1 s en pruebas
        AssertSchema("events.schema.json#/$defs/server", beat!);
        Assert.StartsWith("{\"type\":\"snapshot\"", beat!, StringComparison.Ordinal);

        app.Devices.Revoke(device.DeviceId);
        string? text;
        WebSocketCloseStatus? close;
        do { (text, close) = await Receive(ws); } while (text is not null && !text.Contains("\"error\"", StringComparison.Ordinal));
        if (text is not null) (_, close) = await Receive(ws);
        Assert.Equal(4401, (int)close!.Value);
    }
}

/// <summary>Servidor propio: agota el límite de emparejamiento sin afectar a otras pruebas.</summary>
public class LimiteEmparejamientoTests(ServiceFixture app) : IClassFixture<ServiceFixture>
{
    [Fact]
    public async Task Demasiados_intentos_de_emparejamiento_dan_429()
    {
        var c = app.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 45; i++)
        {
            using var res = await c.PostAsJsonAsync(new Uri("/api/pair", UriKind.Relative), new { code = "000000", deviceName = "x" });
            statuses.Add(res.StatusCode);
        }
        Assert.Equal(40, statuses.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }
}
