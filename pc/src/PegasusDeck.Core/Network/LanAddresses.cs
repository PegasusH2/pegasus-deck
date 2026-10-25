using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace PegasusDeck.Core.Network;

/// <summary>IPv4 privada de una tarjeta de red activa de este PC. <c>HasGateway</c>: es la que sale al router.</summary>
public sealed record LanAddress(IPAddress Address, string Interface, bool HasGateway);

/// <summary>
/// IP de la red local en la que escucha el servicio (ADR-034). Solo se acepta una IPv4 privada que tenga ahora
/// este PC: nunca 0.0.0.0 ni una IP pública (el servicio no debe aparecer fuera de la LAN).
/// </summary>
public static partial class LanAddresses
{
    [GeneratedRegex(@"^\d{1,3}(\.\d{1,3}){3}$")]
    private static partial Regex Ipv4Text();

    /// <summary>IPs privadas de las tarjetas activas; primero las que tienen puerta de enlace.</summary>
    public static IReadOnlyList<LanAddress> Current()
    {
        var list = new List<LanAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var props = nic.GetIPProperties();
                var gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                foreach (var u in props.UnicastAddresses)
                {
                    if (u.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(u.Address)) list.Add(new(u.Address, nic.Name, gateway));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Sin datos de red: lista vacía (la ventana lo explica).
        }
        return [.. list.OrderByDescending(a => a.HasGateway)];
    }

    /// <summary>10.0.0.0/8, 172.16.0.0/12 o 192.168.0.0/16.</summary>
    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    /// <summary>Comprueba la IP escrita. Devuelve la IP o el motivo por el que no vale.</summary>
    public static (IPAddress? Ip, string? Problem) Check(string? text, IReadOnlyList<LanAddress> local)
    {
        text = text?.Trim() ?? "";
        // IPAddress.TryParse admite formas raras («1» = 0.0.0.1): solo cuatro números con puntos.
        if (!Ipv4Text().IsMatch(text) || !IPAddress.TryParse(text, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return (null, "Escribe una IPv4 como 192.168.1.23.");
        }
        if (!IsPrivate(ip)) return (null, "Solo IP de red local (192.168.x.x, 10.x.x.x o 172.16–31.x.x): Pegasus Deck no se expone a Internet.");
        if (!local.Any(a => a.Address.Equals(ip)))
        {
            var now = local.Count == 0 ? "ninguna" : string.Join(", ", local.Select(a => a.Address));
            return (null, $"Este PC no tiene ahora la IP {ip} (tiene: {now}). Ponla primero en Windows o en el router.");
        }
        return (ip, null);
    }
}
