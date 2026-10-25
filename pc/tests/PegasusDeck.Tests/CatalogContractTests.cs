using System.Reflection;
using System.Text.Json;
using PegasusDeck.Core.Actions;
using PegasusDeck.Core.Contract;

namespace PegasusDeck.Tests;

/// <summary>La lista blanca de C# coincide con contracts/actions.json (la PWA tiene la prueba equivalente).</summary>
public class CatalogoContratoTests
{
    private static readonly JsonElement Specs =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Contracts.Dir, "actions.json"))).RootElement;

    private static IEnumerable<JsonProperty> Actions => Specs.EnumerateObject().Where(p => !p.Name.StartsWith('$'));

    private static ActionRequest ToRequest(string type, JsonElement p) =>
        new(type, p.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.ValueKind switch
        {
            JsonValueKind.String => (object)x.Value.GetString()!,
            JsonValueKind.Number => x.Value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => x.Value.ToString(),
        }));

    [Fact]
    public void Mismos_tipos_que_el_contrato()
    {
        Assert.Equal(Actions.Select(a => a.Name).Order(), ActionCatalog.All.Select(a => a.Type).Order());
    }

    [Fact]
    public void Mismos_parametros_y_casos_validos_e_invalidos()
    {
        foreach (var action in Actions)
        {
            var spec = ActionCatalog.Find(action.Name)!;
            var expected = action.Value.GetProperty("params").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
            var actual = spec.Params.ToDictionary(p => p.Key, p => p.Kind switch
            {
                ParamKind.AppId => "id",
                ParamKind.Percent => "percent",
                ParamKind.ResolveCommand => "resolveCommand",
                _ => "name",
            });
            Assert.Equal(expected.OrderBy(x => x.Key), actual.OrderBy(x => x.Key));
            Assert.Equal(action.Value.GetProperty("completion").GetString(), spec.Completion == ActionCompletion.Event ? "event" : "response");
            Assert.Equal(action.Value.GetProperty("timeoutMs").GetInt32(), spec.TimeoutMs);
            Assert.Equal(action.Value.TryGetProperty("destructive", out var d) && d.GetBoolean(), spec.Destructive);

            foreach (var v in action.Value.GetProperty("valid").EnumerateArray())
            {
                Assert.True(ActionCatalog.Validate(ToRequest(action.Name, v)) is null, $"{action.Name} {v}");
            }
            foreach (var v in action.Value.GetProperty("invalid").EnumerateArray())
            {
                Assert.Equal(ErrorCodes.BadParams, ActionCatalog.Validate(ToRequest(action.Name, v))?.Code);
            }
        }
    }

    [Fact]
    public void Codigos_de_error_iguales_al_esquema_comun()
    {
        var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(Contracts.Dir, "schemas", "common.schema.json"))).RootElement;
        var expected = schema.GetProperty("$defs").GetProperty("errorCode").GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).Order();
        var actual = typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetValue(null)!).Order();
        Assert.Equal(expected, actual);
    }
}
