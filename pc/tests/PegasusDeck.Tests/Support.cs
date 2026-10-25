using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace PegasusDeck.Tests;

/// <summary>Acceso a contracts/ (esquemas y ejemplos compartidos con la PWA).</summary>
public static class Contracts
{
    private const string Base = "https://pegasus-deck.local/contracts/";
    private static readonly object Lock = new();
    private static bool _registered;

    public static string Dir { get; } = FindContracts();

    private static string FindContracts()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "contracts");
            if (Directory.Exists(Path.Combine(candidate, "schemas"))) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("No se encuentra contracts/");
    }

    private static void EnsureRegistered()
    {
        lock (Lock)
        {
            if (_registered) return;
            foreach (var file in Directory.GetFiles(Path.Combine(Dir, "schemas"), "*.json"))
            {
                SchemaRegistry.Global.Register(JsonSchema.FromText(File.ReadAllText(file)));
            }
            _registered = true;
        }
    }

    /// <summary>Valida un JSON contra un esquema del contrato (p. ej. "action.schema.json#/$defs/response").</summary>
    public static (bool Valid, string Errors) Validate(string schemaRef, string json)
    {
        EnsureRegistered();
        var wrapper = JsonSchema.FromText($$"""{"$ref":"{{Base}}{{schemaRef}}"}""");
        var result = wrapper.Evaluate(JsonNode.Parse(json), new EvaluationOptions { OutputFormat = OutputFormat.List });
        var errors = string.Join("; ", (result.Details ?? []).Where(d => d.Errors is not null)
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}")));
        return (result.IsValid, errors);
    }

    public static string SchemaFor(string target)
    {
        var index = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Dir, "examples", "index.json")))!;
        return index[target];
    }

    public static IEnumerable<object[]> Examples(string target, string kind) =>
        Directory.GetFiles(Path.Combine(Dir, "examples", target, kind), "*.json")
            .Select(f => new object[] { Path.GetFileName(f), File.ReadAllText(f) });
}

public sealed class FakeTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("pegasus-tests-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
