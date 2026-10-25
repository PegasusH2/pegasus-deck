using PegasusDeck.Core.Contract;
using PegasusDeck.Core.Security;

namespace PegasusDeck.Service;

internal static class ApiEndpoints
{
    private const int MaxBody = 16 * 1024;

    /// <summary>Límite de intentos de emparejamiento por IP (además de los 5 por código).</summary>
    public sealed class PairLimiter(Microsoft.Extensions.Options.IOptions<PegasusOptions> options)
    {
        public RateLimiter Limiter { get; } = new(Math.Max(1, options.Value.PairAttemptsPerMinute), TimeSpan.FromMinutes(1));
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/health", (SystemState state) =>
            JsonResults.Ok(new HealthResponse(Protocol.Version, state.Service)));

        app.MapPost("/api/pair", PairAsync);
        app.MapPost("/api/actions", ActionAsync);
        app.Map("/api/events", (HttpContext ctx, EventsHub hub) => hub.HandleAsync(ctx));

        // Cualquier otra ruta /api: 404 en JSON (nunca la PWA).
        app.Map("/api/{**rest}", () => JsonResults.Error(ErrorCodes.NotFound, "No encontrado."));
    }

    private static async Task<IResult> PairAsync(
        HttpContext ctx, PairingStore pairing, DeviceStore devices, PairLimiter limiter, ILoggerFactory logs)
    {
        var log = logs.CreateLogger("PegasusDeck.Pairing");
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
        if (!limiter.Limiter.TryAcquire(RateKey(ctx.Connection.RemoteIpAddress))) return JsonResults.Error(ErrorCodes.RateLimited, "Demasiados intentos. Espera un minuto.");

        using var doc = await JsonResults.ReadJsonAsync(ctx.Request, MaxBody, ctx.RequestAborted);
        if (doc is null) return JsonResults.Error(ErrorCodes.BadJson, "JSON no válido.");
        var req = RequestParser.ParsePair(doc.RootElement);
        if (!req.Ok) return JsonResults.Error(ErrorCodes.BadParams, req.Error!);

        if (pairing.TryConsume(req.Value!.Code) != PairingCheck.Ok)
        {
            Log.PairingFailed(log, ip);
            return JsonResults.Error(ErrorCodes.PairingInvalid, "Código incorrecto o caducado. Genera uno nuevo en el PC.");
        }
        var (device, token) = devices.Add(req.Value.DeviceName);
        Log.Paired(log, device.DeviceId);
        ctx.RequestServices.GetService<Agent.PipeAgentLink>()?.NotifyPaired(req.Value.DeviceName);
        return JsonResults.Ok(new PairSuccess(device.DeviceId, token));
    }

    /// <summary>Clave del límite: la IPv4, o el prefijo /64 en IPv6 (un equipo puede rotar direcciones dentro de él).</summary>
    internal static string RateKey(System.Net.IPAddress? ip)
    {
        if (ip is null) return "?";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return ip.ToString();
        return Convert.ToHexString(ip.GetAddressBytes(), 0, 8) + "::/64";
    }

    private static async Task<IResult> ActionAsync(HttpContext ctx, DeviceStore devices, ActionDispatcher dispatcher)
    {
        var auth = ctx.Request.Headers.Authorization.ToString();
        var device = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? devices.FindByToken(auth[7..]) : null;
        if (device is null) return JsonResults.Error(ErrorCodes.AuthRequired, "Token no válido o revocado.");

        using var doc = await JsonResults.ReadJsonAsync(ctx.Request, MaxBody, ctx.RequestAborted);
        if (doc is null) return JsonResults.Error(ErrorCodes.BadJson, "JSON no válido.");
        var req = RequestParser.ParseAction(doc.RootElement);
        if (!req.Ok) return JsonResults.Error(ErrorCodes.BadParams, req.Error!);

        return await dispatcher.DispatchAsync(req.Value!, device.DeviceId, ctx.RequestAborted) switch
        {
            ActionOutcome.Completed c => JsonResults.Ok(new CompletedResponse(c.Message)),
            ActionOutcome.Accepted a => JsonResults.Ok(new AcceptedResponse(a.OperationId, a.Message)),
            ActionOutcome.ConfirmRequired c => JsonResults.Ok(new ConfirmRequiredResponse(c.Token, c.ExpiresInSec, c.Message)),
            ActionOutcome.Failed f => JsonResults.Error(f.Code, f.Message),
            _ => JsonResults.Error(ErrorCodes.Internal, "Error interno."),
        };
    }
}
