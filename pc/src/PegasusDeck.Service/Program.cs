// Servicio de Windows de Pegasus Deck (sesión 0): API HTTPS en la LAN, emparejamiento,
// canal de eventos y PWA. Lo que toca el escritorio lo hará el agente de sesión (fase 3).

using PegasusDeck.Service;

var builder = ServiceHost.CreateBuilder(args);
if (Cli.TryRun(args, builder.Configuration, out var exitCode)) return exitCode;

var app = ServiceHost.Build(builder);
await app.RunAsync();
return 0;

/// <summary>Punto de entrada visible para las pruebas de integración.</summary>
public partial class Program;
