using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PegasusDeck.Agent.Apps;
using PegasusDeck.Core.Apps;
using PegasusDeck.Core.Contract;
using PegasusDeck.Service;

namespace PegasusDeck.Tests;

/// <summary>ADR-023: añadir y quitar apps autorizadas (menú del agente → orden local del servicio con UAC).</summary>
public sealed class AppsEditorTests : IDisposable
{
    private readonly TempDir _data = new();
    private static readonly string Notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
    private static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
    private static readonly string Explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    public void Dispose() => _data.Dispose();

    private string AppsPath => Path.Combine(_data.Path, AppsFile.FileName);

    private IReadOnlyList<AppEntry> Stored() => AppsFile.Load(_data.Path).Apps;

    [Fact]
    public void Anade_un_ejecutable_con_id_seguro_y_nombre_de_su_ficha()
    {
        var r = AppsEditor.Add(_data.Path, Notepad, null, allowUnprotectedPath: false);
        Assert.True(r.Ok, r.Message);
        var app = Assert.Single(Stored());
        Assert.Equal(r.Id, app.Id);
        Assert.Matches("^[a-z0-9][a-z0-9-]{0,39}$", app.Id);
        Assert.Equal(Notepad, app.ExePath, ignoreCase: true);
        Assert.Empty(app.Args);
        Assert.Equal("window", app.ReadyLevel);
        Assert.False(app.AllowUnprotectedPath);
    }

    [Fact]
    public void Rechaza_interpretes_rutas_relativas_inexistentes_y_duplicados()
    {
        Assert.Contains("cmd.exe no está permitido", AppsEditor.Add(_data.Path, Cmd, "Consola", false).Message);
        Assert.False(AppsEditor.Add(_data.Path, @"..\notepad.exe", "x", false).Ok);
        Assert.False(AppsEditor.Add(_data.Path, @"C:\NoExiste\nada.exe", "x", false).Ok);
        Assert.False(AppsEditor.Add(_data.Path, @"\\servidor\share\app.exe", "x", false).Ok);
        Assert.True(AppsEditor.Add(_data.Path, Notepad, "Bloc", false).Ok);
        Assert.Contains("Ya está autorizada", AppsEditor.Add(_data.Path, Notepad, "Otra vez", false).Message);
        Assert.Single(Stored());
    }

    [Fact]
    public void Ids_unicos_sin_tildes_y_el_explorador_siempre_abre_ventana_nueva()
    {
        Assert.Equal("cafe-musica", AppsEditor.NewId("Café Música!", []));
        Assert.Equal("obs-2", AppsEditor.NewId("OBS", ["obs"]));
        Assert.Equal("app", AppsEditor.NewId("¡¡!!", []));
        var r = AppsEditor.Add(_data.Path, Explorer, "Explorador", false);
        Assert.True(r.Ok, r.Message);
        var e = Assert.Single(Stored());
        Assert.True(e.AlwaysLaunch);
        Assert.Equal("process", e.ReadyLevel);
    }

    [Fact]
    public void Quitar_conserva_las_demas_entradas_incluso_las_no_validas()
    {
        var others = new[]
        {
            new AppEntry("obs", "OBS", @"C:\Program Files\obs-studio\bin\64bit\obs64.exe", [], "window", false),
            new AppEntry("rara", "Rara", "relativa.exe", [], "window", false), // no válida: no se ejecuta, pero no se pierde
        };
        File.WriteAllText(AppsPath, JsonSerializer.Serialize(others, JsonDefaults.Options));
        var added = AppsEditor.Add(_data.Path, Notepad, "Bloc", false);
        Assert.True(added.Ok);

        var removed = AppsEditor.Remove(_data.Path, added.Id!);
        Assert.True(removed.Ok);
        var raw = JsonSerializer.Deserialize<List<AppEntry>>(File.ReadAllText(AppsPath), JsonDefaults.Options)!;
        Assert.Equal(["obs", "rara"], raw.Select(a => a.Id));
        Assert.False(AppsEditor.Remove(_data.Path, "no-existe").Ok);
    }

    [Fact]
    public void Un_apps_json_danado_no_se_sobrescribe()
    {
        File.WriteAllText(AppsPath, "{ esto no es json");
        Assert.False(AppsEditor.Add(_data.Path, Notepad, "Bloc", false).Ok);
        Assert.Equal("{ esto no es json", File.ReadAllText(AppsPath));
    }

    [Fact]
    public void Orden_local_apps_add_remove_con_codigos_de_salida()
    {
        int Run(params string[] args)
        {
            var all = args.Concat([$"--Pegasus:DataDir={_data.Path}", "--Pegasus:UnprotectedDataDir=true"]).ToArray();
            var config = new ConfigurationBuilder().AddCommandLine(all).Build();
            Assert.True(Cli.TryRun(all, config, out var code));
            return code;
        }

        Assert.Equal(0, Run("apps", "add", $"--exe={Notepad}", "--name=Bloc de notas"));
        var app = Assert.Single(Stored());
        Assert.Equal("Bloc de notas", app.Name);
        Assert.Equal(1, Run("apps", "add", $"--exe={Cmd}"));
        Assert.Equal(0, Run("apps", "remove", app.Id));
        Assert.Empty(Stored());
        Assert.Equal(1, Run("apps", "remove", app.Id));
        Assert.Equal(2, Run("apps", "otra"));
    }

    [Fact]
    public void Pair_con_code_file_deja_el_codigo_para_la_ventana_y_no_sobrescribe_otros_archivos()
    {
        int Run(params string[] args)
        {
            var all = args.Concat([$"--Pegasus:DataDir={_data.Path}", "--Pegasus:UnprotectedDataDir=true"]).ToArray();
            Assert.True(Cli.TryRun(all, new ConfigurationBuilder().AddCommandLine(all).Build(), out var code));
            return code;
        }

        var file = Path.GetTempFileName();
        try
        {
            Assert.Equal(0, Run("pair", $"--code-file={file}"));
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            Assert.Matches("^[0-9]{6}$", doc.RootElement.GetProperty("code").GetString()!);
            Assert.True(doc.RootElement.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.Now);
            // Ya tiene contenido: no se reescribe (la orden corre con permisos de administrador).
            Assert.Equal(2, Run("pair", $"--code-file={file}"));
            Assert.Equal(2, Run("pair", $"--code-file={Path.Combine(_data.Path, "no-existe.tmp")}"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Programas_del_menu_Inicio_solo_ejecutables_autorizables()
    {
        // Solo lectura de los accesos directos del menú Inicio de este PC.
        foreach (var p in InstalledPrograms.Scan())
        {
            Assert.EndsWith(".exe", p.ExePath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(p.ExePath));
            Assert.NotNull(AppsEditor.Prepare([], p.ExePath, p.Name, true).Entry);
        }
    }
}

/// <summary>ADR-027: los juegos se reconocen por su carpeta de instalación (solo para el icono del iPad).</summary>
public class DeteccionDeJuegosTests
{
    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Call of Duty HQ\cod.exe", true)]
    [InlineData(@"D:\SteamLibrary\steamapps\common\Grand Theft Auto V\GTA5.exe", true)]
    [InlineData(@"C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe", true)]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe", false)]
    [InlineData(@"C:\Program Files (x86)\Battle.net\Battle.net.exe", false)]
    [InlineData(@"C:\Program Files\obs-studio\bin\64bit\obs64.exe", false)]
    public void Juego_por_carpeta(string path, bool game) => Assert.Equal(game, PegasusDeck.Agent.Apps.AppsMonitor.IsGame(path));
}

/// <summary>ADR-028: órdenes de DaVinci Resolve (sin pulsar teclas de verdad en las pruebas).</summary>
public class OrdenesDeResolveTests
{
    [Fact]
    public void Mismos_ids_que_el_contrato_y_cada_uno_con_tecla()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Contracts.Dir, "resolve-commands.json")));
        var ids = doc.RootElement.EnumerateObject().Select(p => p.Name).Where(n => !n.StartsWith('$')).Order().ToList();
        Assert.Equal(ids, PegasusDeck.Core.Resolve.ResolveCommands.All.Keys.Order().ToList());
        Assert.All(PegasusDeck.Core.Resolve.ResolveCommands.All.Values, k => Assert.NotEqual(0, k.DefaultKey));
    }

    [Fact]
    public void La_estructura_de_teclado_mide_lo_que_espera_Windows_x64()
    {
        Assert.Equal(40, System.Runtime.InteropServices.Marshal.SizeOf<PegasusDeck.Agent.Windows.NativeMethods.KeyInput>());
    }
}

/// <summary>ADR-030: apps de Microsoft Store / MSIX (solo lectura: las pruebas no abren ninguna app).</summary>
public sealed class AppsDeStoreTests : IDisposable
{
    private readonly TempDir _data = new();
    private static readonly string Notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Aumid_valido_se_guarda_y_no_se_duplica()
    {
        var r = AppsEditor.Add(_data.Path, Notepad, "Claude", false, "Claude_pzs8sxrjxfjjc!Claude");
        Assert.True(r.Ok, r.Message);
        Assert.Equal("Claude_pzs8sxrjxfjjc!Claude", Assert.Single(AppsFile.Load(_data.Path).Apps).Aumid);
        Assert.Contains("Ya está autorizada", AppsEditor.Add(_data.Path, Notepad, "Claude otra", false, "Claude_pzs8sxrjxfjjc!Claude").Message);
    }

    [Theory]
    [InlineData("Claude")]
    [InlineData("Claude_pzs8sxrjxfjjc")]
    [InlineData("cmd.exe /c calc")]
    [InlineData("Claude_pzs8sxrjxfjjc!Claude --inject")]
    [InlineData(@"..\x_pzs8sxrjxfjjc!App")]
    public void Aumid_no_valido_se_rechaza(string aumid) => Assert.False(AppsEditor.Add(_data.Path, Notepad, "X", false, aumid).Ok);

    [Fact]
    public void Una_app_de_Store_no_admite_argumentos()
    {
        var (_, errors) = AppsFile.Validate([new AppEntry("x", "X", Notepad, ["--algo"], "window", false, Aumid: "Claude_pzs8sxrjxfjjc!Claude")]);
        Assert.Contains(errors, e => e.Contains("no admite argumentos", StringComparison.Ordinal));
    }

    [Fact]
    public void Apps_de_Store_instaladas_con_aumid_valido_y_carpeta_actual()
    {
        foreach (var p in PegasusDeck.Agent.Apps.PackagedApps.Scan())
        {
            Assert.Matches(AppsFile.AumidRegex(), p.Aumid!);
            Assert.True(File.Exists(p.ExePath), p.ExePath);
            var dir = PegasusDeck.Agent.Apps.PackagedApps.InstallDir(p.Aumid!);
            Assert.NotNull(dir);
            Assert.StartsWith(dir!, p.ExePath, StringComparison.OrdinalIgnoreCase);
        }
    }
}

/// <summary>ADR-031: atajos del preset de teclado activo del usuario en Resolve (solo lectura).</summary>
public sealed class MapaDeTeclasDeResolveTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static byte[] Str(string s)
    {
        var b = System.Text.Encoding.BigEndianUnicode.GetBytes(s);
        return [.. Int(b.Length), .. b];
    }

    private static byte[] Int(int v) { var b = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b, v); return b; }

    private static byte[] Preset(params (string Name, int[] Keys)[] commands)
    {
        var body = new List<byte>([.. Int(1), .. Int(commands.Length)]);
        foreach (var (name, keys) in commands)
        {
            body.AddRange(Str(name));
            body.AddRange(Int(keys.Length));
            foreach (var k in keys) body.AddRange([.. Int(1), .. Int(k)]);
        }
        return [.. body];
    }

    private void Write(string active, params (string Name, byte[] Data)[] presets)
    {
        var list = new List<byte>([.. Int(1), .. Int(presets.Length)]);
        foreach (var (name, data) in presets) list.AddRange([.. Str(name), .. Int(data.Length), .. data]);
        File.WriteAllText(Path.Combine(_dir.Path, "keyboard.preset.xml"), $"<SmKeyboardPresetList><PresetListBA>{Convert.ToHexString([.. list]).ToLowerInvariant()}</PresetListBA></SmKeyboardPresetList>");
        File.WriteAllText(Path.Combine(_dir.Path, "config.user.xml"), $"<x><CustomKeyboardPreset>{active}</CustomKeyboardPreset></x>");
    }

    [Fact]
    public void Usa_el_preset_activo_y_sus_teclas_y_dice_si_una_orden_no_tiene_atajo()
    {
        const int shift = PegasusDeck.Core.Resolve.ResolveCommands.Shift;
        Write("Mío",
            ("Otro", Preset(("workspacePrimaryWorkspaceColor", [shift | '9']))),
            ("Mío", Preset(("workspacePrimaryWorkspaceColor", [shift | '1']), ("PhotoPanel..photoTagGoodTake", []))));
        var map = PegasusDeck.Core.Resolve.ResolveKeymap.Load(_dir.Path);
        Assert.Equal("Mío", map!.PresetName);
        var all = PegasusDeck.Core.Resolve.ResolveCommands.All;
        Assert.Equal(shift | '1', PegasusDeck.Core.Resolve.ResolveKeymap.Resolve(map, all["page.color"]).Key);
        Assert.Null(PegasusDeck.Core.Resolve.ResolveKeymap.Resolve(map, all["photo.like"]).Key); // existe pero sin atajo
        Assert.Equal(all["project.save"].DefaultKey, PegasusDeck.Core.Resolve.ResolveKeymap.Resolve(map, all["project.save"]).Key); // no está: por defecto
    }

    [Fact]
    public void Sin_archivos_o_dañados_se_usan_los_atajos_por_defecto()
    {
        Assert.Null(PegasusDeck.Core.Resolve.ResolveKeymap.Load(_dir.Path));
        File.WriteAllText(Path.Combine(_dir.Path, "config.user.xml"), "<x><CustomKeyboardPreset>A</CustomKeyboardPreset></x>");
        File.WriteAllText(Path.Combine(_dir.Path, "keyboard.preset.xml"), "<PresetListBA>zz</PresetListBA>");
        Assert.Null(PegasusDeck.Core.Resolve.ResolveKeymap.Load(_dir.Path));
    }

    [Fact]
    public void El_mapa_real_de_este_PC_se_lee_si_existe()
    {
        var map = PegasusDeck.Core.Resolve.ResolveKeymap.Load();
        if (map is null) return; // PC sin Resolve o sin preset personalizado
        Assert.True(map.Keys.Count > 100);
        Assert.Contains("workspacePrimaryWorkspaceColor", map.Keys.Keys);
    }
}
