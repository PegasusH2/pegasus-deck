using PegasusDeck.Core.Apps;

namespace PegasusDeck.Tests;

public class ListaBlancaDeAppsTests
{
    private static AppEntry App(string id = "obs", string exe = @"C:\Program Files\obs-studio\bin\64bit\obs64.exe", string[]? args = null, string ready = "window") =>
        new(id, "OBS", exe, args ?? [], ready, false);

    [Fact]
    public void Entrada_valida_se_acepta()
    {
        var (apps, errors) = AppsFile.Validate([App()]);
        Assert.Single(apps);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")]
    [InlineData(@"C:\Windows\System32\rundll32.exe")]
    [InlineData(@"C:\Windows\System32\mshta.exe")]
    [InlineData(@"obs64.exe")]
    [InlineData(@"C:\Apps\..\Windows\notepad.exe")]
    [InlineData(@"\\servidor\recurso\app.exe")]
    [InlineData(@"C:\Apps\script.bat")]
    [InlineData(@"C:\Windows\System32\conhost.exe")]
    [InlineData(@"C:\Windows\System32\forfiles.exe")]
    [InlineData(@"C:\Windows\System32\msiexec.exe")]
    public void Ejecutables_peligrosos_o_rutas_dudosas_se_descartan(string exe)
    {
        var (apps, errors) = AppsFile.Validate([App(exe: exe)]);
        Assert.Empty(apps);
        Assert.Single(errors);
    }

    [Fact]
    public void Ids_no_validos_duplicados_y_argumentos_con_control_se_descartan()
    {
        var (apps, errors) = AppsFile.Validate([App(id: "OBS"), App(), App(), App(id: "x", args: ["\u001b[2J"]), App(id: "y", ready: "maybe")]);
        Assert.Single(apps);
        Assert.Equal(4, errors.Count);
    }

    [Fact]
    public void Explorador_con_argumentos_se_descarta()
    {
        var (apps, _) = AppsFile.Validate([new AppEntry("e", "Explorador", @"C:\Windows\explorer.exe", [@"C:\x\p.bat"], "process", true)]);
        Assert.Empty(apps);
    }

    [Fact]
    public void Permisos_del_ejecutable()
    {
        // Un ejecutable del sistema solo lo pueden modificar administradores / TrustedInstaller.
        Assert.Null(PegasusDeck.Core.Security.ExecutableSecurity.Problem(Path.Combine(Environment.SystemDirectory, "notepad.exe")));
        // Uno en una carpeta del usuario, no.
        using var dir = new TempDir();
        var exe = Path.Combine(dir.Path, "app.exe");
        File.WriteAllBytes(exe, [0x4D, 0x5A]);
        Assert.NotNull(PegasusDeck.Core.Security.ExecutableSecurity.Problem(exe));
    }

    [Fact]
    public void Las_apps_por_defecto_de_este_PC_estan_en_carpetas_protegidas()
    {
        // Si no lo estuvieran, el agente se negaría a abrirlas sin permiso expreso.
        using var dir = new TempDir();
        foreach (var app in AppsFile.CreateDefault(dir.Path))
        {
            Assert.True(PegasusDeck.Core.Security.ExecutableSecurity.Problem(app.ExePath) is null,
                $"{app.ExePath}: {PegasusDeck.Core.Security.ExecutableSecurity.Problem(app.ExePath)}");
        }
    }

    [Fact]
    public void Archivo_dañado_no_autoriza_nada()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, AppsFile.FileName), "{ esto no es json");
        var (apps, errors) = AppsFile.Load(dir.Path);
        Assert.Empty(apps);
        Assert.Single(errors);
    }

    [Fact]
    public void La_lista_inicial_solo_incluye_lo_instalado()
    {
        using var dir = new TempDir();
        var apps = AppsFile.CreateDefault(dir.Path);
        Assert.All(apps, a => Assert.True(File.Exists(a.ExePath)));
        Assert.Contains(apps, a => a.Id == "explorer" && a.AlwaysLaunch);
        Assert.Equal(apps.Count, AppsFile.Load(dir.Path).Apps.Count);
    }
}
