using System.Text.Json;
using Microsoft.Extensions.Options;
using PegasusDeck.Core.Contract;

namespace PegasusDeck.Service;

/// <summary>
/// Primera barrera de cada petición:
///  - Host debe ser uno de los configurados (contra DNS rebinding).
///  - Si hay Origin, debe coincidir exactamente con el de la PWA (contra CSRF desde otras webs).
///  - Cabeceras de seguridad y sin caché en la API.
/// </summary>
public sealed class RequestGuard(RequestDelegate next, IOptions<PegasusOptions> options, ILogger<RequestGuard> log)
{
    private readonly HashSet<string> _hosts = new(options.Value.AllowedHosts, StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlySet<string> _origins = options.Value.AllowedOrigins();
    private readonly object _logLock = new();
    private DateTimeOffset _lastLogged = DateTimeOffset.MinValue;
    private int _suppressed;

    /// <summary>Un aviso detallado por minuto como máximo; el resto se cuenta (no se inunda el registro).</summary>
    private bool ShouldLog()
    {
        lock (_logLock)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastLogged < TimeSpan.FromMinutes(1))
            {
                _suppressed++;
                return false;
            }
            if (_suppressed > 0) Log.GuardRejectionsSuppressed(log, _suppressed);
            _suppressed = 0;
            _lastLogged = now;
            return true;
        }
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var host = ctx.Request.Host.Host;
        if (!_hosts.Contains(host))
        {
            if (ShouldLog()) Log.HostRejected(log, Sanitize(host));
            await Forbidden(ctx, ErrorCodes.HostForbidden, "Host no autorizado.");
            return;
        }

        var origin = ctx.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && !_origins.Contains(origin))
        {
            if (ShouldLog()) Log.OriginRejected(log, Sanitize(origin));
            await Forbidden(ctx, ErrorCodes.OriginForbidden, "Origen no autorizado.");
            return;
        }

        var headers = ctx.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        if (ctx.Request.Path.StartsWithSegments("/api")) headers.CacheControl = "no-store";

        await next(ctx);
    }

    private static Task Forbidden(HttpContext ctx, string code, string message)
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return ctx.Response.WriteAsJsonAsync(new ErrorResponse(code, message), JsonDefaults.Options);
    }

    private static string Sanitize(string value)
    {
        var clean = new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        return clean.Length > 100 ? clean[..100] : clean;
    }
}

internal static class JsonResults
{
    public static IResult Ok<T>(T value) => Results.Json(value, JsonDefaults.Options);

    public static IResult Error(string code, string message) =>
        Results.Json(new ErrorResponse(code, message), JsonDefaults.Options, statusCode: StatusFor(code));

    public static int StatusFor(string code) => code switch
    {
        ErrorCodes.AuthRequired or ErrorCodes.PairingInvalid => 401,
        ErrorCodes.OriginForbidden or ErrorCodes.HostForbidden or ErrorCodes.AppNotAllowed => 403,
        ErrorCodes.NotFound or ErrorCodes.AppNotFound => 404,
        ErrorCodes.NoSession or ErrorCodes.ObsNotReady or ErrorCodes.Conflict or ErrorCodes.Busy or ErrorCodes.ConfirmationInvalid => 409,
        ErrorCodes.RateLimited => 429,
        ErrorCodes.Timeout => 504,
        ErrorCodes.Internal => 500,
        _ => 400,
    };

    /// <summary>Lee un JSON del cuerpo con tamaño máximo; null si no es JSON válido o es demasiado grande.</summary>
    public static async Task<JsonDocument?> ReadJsonAsync(HttpRequest req, int maxBytes, CancellationToken ct)
    {
        if (req.ContentLength > maxBytes) return null;
        var buffer = new byte[maxBytes + 1];
        var total = 0;
        int read;
        while ((read = await req.Body.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct)) > 0)
        {
            total += read;
            if (total > maxBytes) return null;
        }
        try
        {
            return JsonDocument.Parse(buffer.AsMemory(0, total), new JsonDocumentOptions { MaxDepth = 8 });
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
