using System.IO.Pipes;
using System.Security.Principal;
using PegasusDeck.Agent.Audio;
using PegasusDeck.Core.Native;

namespace PegasusDeck.Tests;

/// <summary>Comprobaciones nativas que sí se pueden hacer sin instalar nada (solo lectura).</summary>
public class NativeTests
{
    [Fact]
    public async Task El_cliente_lee_el_propietario_del_pipe()
    {
        var name = $"PegasusDeck.Test.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var wait = server.WaitForConnectionAsync();
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        await client.ConnectAsync(5000);
        await wait;

        using var me = WindowsIdentity.GetCurrent();
        Assert.Equal(me.User, ProcessIdentity.PipeOwner(client));
        // Un usuario normal no es una cuenta de servicio: un agente de producción no confiaría en este pipe.
        Assert.False(ProcessIdentity.IsServiceAccount(me.User));
        Assert.Equal(System.Diagnostics.Process.GetCurrentProcess().SessionId, ProcessIdentity.ServerSessionId(client));
    }

    [Fact]
    public void Cuentas_de_servicio_reconocidas()
    {
        Assert.True(ProcessIdentity.IsServiceAccount(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)));
        Assert.True(ProcessIdentity.IsServiceAccount(new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null)));
        Assert.True(ProcessIdentity.IsServiceAccount(new SecurityIdentifier("S-1-5-80-1234-5678")));
        Assert.False(ProcessIdentity.IsServiceAccount(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null)));
        Assert.False(ProcessIdentity.IsServiceAccount(null));
    }

    [Fact]
    public void Leer_el_audio_real_no_lanza_y_no_lo_modifica()
    {
        // Solo lectura: nunca se cambia el volumen real en pruebas.
        var a = new CoreAudioController().Read();
        Assert.True(a.State is "ready" or "unavailable");
        Assert.InRange(a.Volume, 0, 100);
        Assert.Equal(a, new CoreAudioController().Read());
    }

    [Fact]
    public void Dueño_de_un_puerto_que_escucha()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        Assert.Equal(Environment.ProcessId, TcpListeners.OwnerOf(port));
        listener.Stop();
    }
}
