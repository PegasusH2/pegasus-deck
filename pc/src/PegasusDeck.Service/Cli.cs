using System.Text.Json;
using PegasusDeck.Core.Apps;
using PegasusDeck.Core.Security;

namespace PegasusDeck.Service;

/// <summary>
/// Órdenes locales (las ejecuta alguien delante del PC):
///   PegasusDeck.Service pair [--wait | --code-file=&lt;archivo vacío&gt;]  genera un código de emparejamiento
///     (--code-file: lo usa la ventana "Emparejar iPad…" del agente para mostrarlo sin consola)
///   PegasusDeck.Service devices list          lista los iPad emparejados
///   PegasusDeck.Service devices revoke &lt;id&gt;  revoca uno
///   PegasusDeck.Service apps list                         lista las apps autorizadas
///   PegasusDeck.Service apps add --exe=&lt;ruta&gt; [--name=&lt;nombre&gt;] [--allow-unprotected] [--aumid=&lt;app de Store&gt;]
///   PegasusDeck.Service apps remove &lt;id&gt;
///   PegasusDeck.Service address set &lt;ip&gt; [--ca-root=&lt;carpeta de mkcert&gt;] [--result-file=&lt;archivo vacío&gt;]
///     cambia la IP de la LAN (ADR-034); --result-file: la ventana del agente lee ahí el resultado
/// Las de apps las lanza el menú "Aplicaciones autorizadas…" del agente con UAC (ADR-023).
/// Códigos de salida: 0 bien · 1 rechazada · 2 uso incorrecto · 3 sin permisos sobre la carpeta de datos
/// · 4 sin la CA de mkcert · 5 el servicio no arranca con la IP nueva (se deja como estaba).
/// </summary>
internal static class Cli
{
    public static bool TryRun(string[] args, IConfiguration config, out int exitCode)
    {
        exitCode = 0;
        var positional = args.Where(a => !a.StartsWith('-') && !a.Contains('=', StringComparison.Ordinal)).ToArray();
        if (positional.Length == 0 || positional[0] is not ("pair" or "devices" or "apps" or "address")) return false;

        var options = config.GetSection(PegasusOptions.Section).Get<PegasusOptions>() ?? new PegasusOptions();
        if (options.UnprotectedDataDir)
        {
            Directory.CreateDirectory(options.DataDir);
        }
        else if (DataDirSecurity.EnsureProtected(options.DataDir) is { } problem)
        {
            Console.Error.WriteLine($"No se puede continuar: {problem}");
            Console.Error.WriteLine("Abre la terminal como administrador para emparejar o gestionar dispositivos y apps.");
            exitCode = 3;
            return true;
        }

        switch (positional)
        {
            case ["pair"]:
                var code = new PairingStore(options.DataDir).Create();
                if (Arg(args, "--code-file") is { } codeFile)
                {
                    // Solo un archivo temporal ya creado y vacío (lo crea el agente): nunca se sobrescribe otro archivo con permisos de administrador.
                    var target = new FileInfo(codeFile);
                    if (!Path.IsPathFullyQualified(codeFile) || !target.Exists || target.Length != 0 || target.Extension is not (".tmp" or ".json"))
                    {
                        Console.Error.WriteLine("--code-file debe ser un archivo temporal vacío.");
                        exitCode = 2;
                        return true;
                    }
                    File.WriteAllText(codeFile, JsonSerializer.Serialize(new { code, expiresAt = DateTimeOffset.Now + PairingStore.Lifetime }));
                    return true;
                }
                Console.WriteLine();
                Console.WriteLine($"  Código de emparejamiento:  {code[..3]} {code[3..]}");
                Console.WriteLine($"  Válido {PairingStore.Lifetime.TotalMinutes:0} minutos, un solo uso, {PairingStore.MaxAttempts} intentos.");
                Console.WriteLine("  Introdúcelo en la PWA: Configuración → Emparejar.");
                Console.WriteLine();
                if (args.Contains("--wait"))
                {
                    // Abierto desde el menú del agente: la ventana no debe cerrarse antes de leer el código.
                    Console.WriteLine("  Pulsa Intro para cerrar.");
                    Console.ReadLine();
                }
                return true;

            case ["devices", "list"]:
                var list = new DeviceStore(options.DataDir).List();
                if (list.Count == 0) Console.WriteLine("No hay dispositivos emparejados.");
                foreach (var d in list) Console.WriteLine($"{d.DeviceId}  {d.Name}  (desde {d.PairedAt.LocalDateTime:g})");
                return true;

            case ["devices", "revoke", var id]:
                if (new DeviceStore(options.DataDir).Revoke(id))
                {
                    Console.WriteLine($"Dispositivo {id} revocado. Se desconectará en unos segundos.");
                }
                else
                {
                    Console.Error.WriteLine($"No existe el dispositivo {id}.");
                    exitCode = 1;
                }
                return true;

            case ["apps", "list"]:
                var (apps, errors) = AppsFile.Load(options.DataDir);
                foreach (var a in apps) Console.WriteLine($"{a.Id}  {a.Name}  {a.ExePath}");
                foreach (var e in errors) Console.Error.WriteLine($"No válida: {e}");
                return true;

            case ["apps", "add"]:
                return Report(AppsEditor.Add(options.DataDir, Arg(args, "--exe") ?? "", Arg(args, "--name"), args.Contains("--allow-unprotected"), Arg(args, "--aumid")), out exitCode);

            case ["apps", "remove", var appId]:
                return Report(AppsEditor.Remove(options.DataDir, appId), out exitCode);

            case ["address", "set", var ip]:
            {
                // La ventana «Dirección IP del PC…» del agente lo lanza con UAC.
                var change = new AddressChange(
                    [Path.Combine(AppContext.BaseDirectory, "appsettings.json"), Path.Combine(options.DataDir, "appsettings.json")],
                    Arg(args, "--ca-root") ?? AddressChange.DefaultCaRoot(), new WindowsServiceRestarter(), Core.Network.LanAddresses.Current);
                var result = change.Run(ip);
                (result.ExitCode == 0 ? Console.Out : Console.Error).WriteLine(result.Message);
                if (Arg(args, "--result-file") is { } resultFile && IsEmptyTempFile(resultFile))
                {
                    File.WriteAllText(resultFile, JsonSerializer.Serialize(new { exitCode = result.ExitCode, message = result.Message }));
                }
                exitCode = result.ExitCode;
                return true;
            }

            default:
                Console.Error.WriteLine("Uso: PegasusDeck.Service pair | devices list | devices revoke <id> | apps list | apps add --exe=<ruta> [--name=<nombre>] [--allow-unprotected] | apps remove <id> | address set <ip>");
                exitCode = 2;
                return true;
        }
    }

    /// <summary>Solo un archivo temporal ya creado y vacío: con permisos de administrador nunca se sobrescribe otro.</summary>
    private static bool IsEmptyTempFile(string path)
    {
        var target = new FileInfo(path);
        return Path.IsPathFullyQualified(path) && target.Exists && target.Length == 0 && target.Extension is ".tmp" or ".json";
    }

    private static bool Report(AppsEditResult result, out int exitCode)
    {
        if (result.Ok) Console.WriteLine(result.Message);
        else Console.Error.WriteLine(result.Message);
        exitCode = result.Ok ? 0 : 1;
        return true;
    }

    private static string? Arg(string[] args, string name) =>
        args.Where(a => a.StartsWith(name + "=", StringComparison.Ordinal)).Select(a => a[(name.Length + 1)..]).LastOrDefault();
}
