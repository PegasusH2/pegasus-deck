using System.Net;
using System.Net.WebSockets;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using PegasusDeck.Core.Contract;
using PegasusDeck.Core.Security;
using PegasusDeck.Service;

namespace PegasusDeck.Tests;

/// <summary>Correcciones de la revisión de seguridad y arquitectura de la fase 2.</summary>
public class EndurecimientoTests
{
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    [Fact]
    public void Acl_protegida_es_aceptada()
    {
        Assert.Null(DataDirSecurity.Check(DataDirSecurity.ProtectedAcl()));
    }

    [Fact]
    public void Acl_donde_Usuarios_pueden_escribir_es_rechazada()
    {
        var acl = DataDirSecurity.ProtectedAcl();
        acl.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.WriteData | FileSystemRights.AppendData, AccessControlType.Allow));
        Assert.NotNull(DataDirSecurity.Check(acl));
    }

    [Fact]
    public void Acl_con_propietario_no_administrador_es_rechazada()
    {
        var acl = DataDirSecurity.ProtectedAcl();
        acl.SetOwner(Users);
        Assert.NotNull(DataDirSecurity.Check(acl));
    }

    [Fact]
    public void Leer_es_suficiente_para_otros_usuarios_no_escribir()
    {
        var acl = DataDirSecurity.ProtectedAcl();
        acl.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        Assert.Null(DataDirSecurity.Check(acl));
        acl.SetOwner(Admins);
        Assert.Null(DataDirSecurity.Check(acl));
    }

    [Fact]
    public void Carpeta_de_ProgramData_heredada_es_rechazada()
    {
        // %ProgramData% deja crear archivos a Usuarios: exactamente el caso que se debe impedir.
        var acl = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)).GetAccessControl();
        Assert.NotNull(DataDirSecurity.Check(acl));
    }

    [Theory]
    [InlineData("""{"salt":"00","codeHash":"x","expiresAt":"9999-12-31T00:00:00+00:00","attempts":0}""")]
    [InlineData("""{"salt":"00","codeHash":"x","expiresAt":"2026-10-18T10:01:00+00:00","attempts":-1000}""")]
    public void Un_pairing_json_manipulado_no_sirve(string tampered)
    {
        using var dir = new TempDir();
        var time = new FakeTime(DateTimeOffset.Parse("2026-10-18T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var store = new PairingStore(dir.Path, time);
        // Un atacante con escritura conoce sal y código: hash coherente, pero caducidad/intentos imposibles.
        var json = tampered.Replace("\"x\"", $"\"{Tokens.Hash("00" + "123456")}\"", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(dir.Path, "pairing.json"), json);
        Assert.Equal(PairingCheck.Invalid, store.TryConsume("123456"));
    }

    [Fact]
    public void Instantanea_hostil_queda_dentro_del_contrato()
    {
        var hostile = new Snapshot(
            Protocol.Version,
            new ServiceInfo(Protocol.ServiceName, new string('9', 80)),
            new WindowsInfo(true, new string('H', 200), -5),
            new SessionInfo(true),
            new ObsInfo("rare_state", false, false, false, new string('S', 300),
                [.. Enumerable.Range(0, 300).Select(i => $"Escena {i}"), new string('x', 101)],
                [new ObsInput(new string('i', 150), false), new ObsInput("Mic/Aux", true)]),
            [new AppInfo("../cmd", "x", "ready"), new AppInfo("obs", new string('n', 90), "ready"), new AppInfo("chrome", "Chrome", "zombie")],
            "maybe",
            new AudioInfo("ready", false, 250));

        var clamped = SnapshotLimits.Clamp(hostile);
        var json = JsonSerializer.Serialize(clamped, JsonDefaults.Options);
        var (valid, errors) = Contracts.Validate("snapshot.schema.json", json);
        Assert.True(valid, errors);
        Assert.Null(clamped.Obs.Scene);
        Assert.Equal("unavailable", clamped.Obs.State);
        Assert.Equal(200, clamped.Obs.Scenes.Count);
        Assert.Equal(["Mic/Aux"], clamped.Obs.Inputs.Select(i => i.Name));
        Assert.Equal(["obs"], clamped.Apps.Select(a => a.Id));
        Assert.Equal("unavailable", clamped.AppsDetection);
        Assert.Equal(100, clamped.Audio.Volume);
    }

    [Fact]
    public void Limitador_no_crece_sin_limite()
    {
        var time = new FakeTime(DateTimeOffset.UnixEpoch);
        var limiter = new RateLimiter(3, TimeSpan.FromMinutes(1), time);
        for (var i = 0; i < 300; i++) limiter.TryAcquire($"ip{i}");
        time.Advance(TimeSpan.FromMinutes(2));
        limiter.TryAcquire("nueva");
        Assert.True(limiter.KeyCount < 10, $"claves: {limiter.KeyCount}");
    }

    [Theory]
    [InlineData("192.168.1.50", "192.168.1.50")]
    [InlineData("::ffff:192.168.1.50", "192.168.1.50")]
    [InlineData("fe80::1:2:3:4", "FE80000000000000::/64")]
    [InlineData("fe80::9:9:9:9", "FE80000000000000::/64")]
    public void Clave_de_limite_agrupa_IPv6_por_64(string ip, string expected)
    {
        Assert.Equal(expected, ApiEndpointsAccess.RateKey(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("iPad de Samuel", true)]
    [InlineData("iPad ‮gnas", false)]
    [InlineData("\u001b[2KiPad", false)]
    [InlineData("iPad\r\nadmin", false)]
    public void Nombres_de_dispositivo_seguros_para_la_consola(string name, bool ok)
    {
        Assert.Equal(ok, RequestParser.IsDisplaySafe(name));
    }
}

/// <summary>Acceso a miembros internos del servicio para las pruebas.</summary>
internal static class ApiEndpointsAccess
{
    public static string RateKey(IPAddress ip) => ApiEndpoints.RateKey(ip);
}

public class CanalDeEventosRobustoTests(ServiceFixture app) : IClassFixture<ServiceFixture>
{
    [Fact]
    public async Task Desconexiones_durante_el_latido_no_detienen_el_servicio()
    {
        var (_, token) = app.Devices.Add("estrés");
        var auth = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "auth", token }));

        // Conexiones que se abren y se cortan de golpe durante varios latidos (1 s en pruebas).
        var until = DateTime.UtcNow.AddSeconds(3.5);
        while (DateTime.UtcNow < until)
        {
            var batch = Enumerable.Range(0, 6).Select(async _ =>
            {
                var client = app.Server.CreateWebSocketClient();
                client.ConfigureRequest = r => r.Headers.Origin = ServiceFixture.Origin;
                using var ws = await client.ConnectAsync(new Uri("ws://localhost/api/events"), CancellationToken.None);
                await ws.SendAsync(auth, WebSocketMessageType.Text, true, CancellationToken.None);
                await Task.Delay(Random.Shared.Next(0, 300));
                ws.Abort();
            });
            await Task.WhenAll(batch);
        }

        // El servicio sigue vivo: health responde y un cliente nuevo recibe latidos.
        var health = await app.CreateClient().GetAsync(new Uri("/api/health", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var c = app.Server.CreateWebSocketClient();
        c.ConfigureRequest = r => r.Headers.Origin = ServiceFixture.Origin;
        using var live = await c.ConnectAsync(new Uri("ws://localhost/api/events"), CancellationToken.None);
        await live.SendAsync(auth, WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[64 * 1024];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var types = new List<string>();
        while (types.Count < 2)
        {
            var r = await live.ReceiveAsync(buffer, cts.Token);
            types.Add(JsonDocument.Parse(buffer.AsMemory(0, r.Count)).RootElement.GetProperty("type").GetString()!);
        }
        Assert.Equal(["ready", "snapshot"], types);
    }
}
