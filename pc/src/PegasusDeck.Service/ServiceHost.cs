using System.Net;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using PegasusDeck.Core.Apps;
using PegasusDeck.Core.Native;
using PegasusDeck.Core.Security;
using PegasusDeck.Service.Agent;

namespace PegasusDeck.Service;

internal static class ServiceHost
{
    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            // Como servicio de Windows el directorio de trabajo es System32.
            ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null,
        });
        builder.Host.UseWindowsService(o => o.ServiceName = "PegasusDeck");

        builder.Services.AddOptions<PegasusOptions>().Bind(builder.Configuration.GetSection(PegasusOptions.Section)).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<PegasusOptions>, PegasusOptionsValidator>();

        builder.Services.AddSingleton(sp => new DeviceStore(Opts(sp).DataDir));
        builder.Services.AddSingleton(sp => new PairingStore(Opts(sp).DataDir));
        builder.Services.AddSingleton<ApiEndpoints.PairLimiter>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ServiceEvents>();
        builder.Services.AddSingleton<OperationTracker>();
        builder.Services.AddSingleton<PipeAgentLink>();
        builder.Services.AddSingleton<IAgentLink>(sp => sp.GetRequiredService<PipeAgentLink>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<PipeAgentLink>());
        builder.Services.AddSingleton<SystemState>();
        builder.Services.AddSingleton<ActionDispatcher>();
        builder.Services.AddSingleton<Confirmations>();
        builder.Services.AddSingleton<EventsHub>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<EventsHub>());

        builder.WebHost.ConfigureKestrel((ctx, k) =>
        {
            var o = ctx.Configuration.GetSection(PegasusOptions.Section).Get<PegasusOptions>() ?? new PegasusOptions();
            k.AddServerHeader = false;
            k.Limits.MaxRequestBodySize = 16 * 1024;
            // Un panel doméstico no necesita más: limita el abuso desde la LAN.
            k.Limits.MaxConcurrentConnections = 64;
            k.Limits.MaxConcurrentUpgradedConnections = 16;
            if (o.DevelopmentHttp)
            {
                k.Listen(IPAddress.Loopback, o.Port);
                return;
            }
            var cert = Certificates.Load(o);
            foreach (var address in o.ListenAddresses)
            {
                k.Listen(IPAddress.Parse(address), o.Port, l => l.UseHttps(cert));
            }
        });
        return builder;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        var o = app.Services.GetRequiredService<IOptions<PegasusOptions>>().Value;
        if (o.UnprotectedDataDir)
        {
            Directory.CreateDirectory(o.DataDir);
        }
        else if (DataDirSecurity.EnsureProtected(o.DataDir) is { } problem)
        {
            // No se arranca con datos que otro usuario del PC podría manipular.
            throw new InvalidOperationException($"Carpeta de datos insegura: {problem}");
        }
        // Lista blanca inicial con las apps conocidas instaladas (el administrador la edita después).
        if (!File.Exists(Path.Combine(o.DataDir, AppsFile.FileName))) AppsFile.CreateDefault(o.DataDir);

        app.UseMiddleware<RequestGuard>();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        ApiEndpoints.Map(app);
        MapPwa(app, o);
        return app;
    }

    /// <summary>Sirve la PWA compilada. index.html, sw.js y el manifiesto sin caché; los assets con hash, inmutables.</summary>
    private static void MapPwa(WebApplication app, PegasusOptions o)
    {
        var root = o.WebRoot ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!Directory.Exists(root))
        {
            Log.PwaMissing(app.Logger, root);
            return;
        }
        var files = new PhysicalFileProvider(Path.GetFullPath(root));
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".webmanifest"] = "application/manifest+json";
        var staticOptions = new StaticFileOptions
        {
            FileProvider = files,
            ContentTypeProvider = types,
            OnPrepareResponse = c =>
            {
                var path = c.Context.Request.Path.Value ?? "";
                c.Context.Response.Headers.CacheControl = path.StartsWith("/assets/", StringComparison.Ordinal)
                    ? "public, max-age=31536000, immutable"
                    : "no-cache";
            },
        };
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(staticOptions);
        app.MapFallbackToFile("index.html", staticOptions);
    }

    private static PegasusOptions Opts(IServiceProvider sp) => sp.GetRequiredService<IOptions<PegasusOptions>>().Value;
}
