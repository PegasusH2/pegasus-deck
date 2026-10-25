using System.ComponentModel;
using System.Diagnostics;
using PegasusDeck.Core.Apps;
using PegasusDeck.Core.Security;

namespace PegasusDeck.Agent.Apps;

/// <summary>
/// "Aplicaciones autorizadas…" (ADR-023): ver, añadir y quitar las apps que se pueden abrir
/// desde el iPad. Se guarda con la orden local del servicio y permiso de administrador
/// (UAC): el agente no puede escribir apps.json por sí mismo, y nada de esto se puede hacer
/// desde el iPad. Antes de pedir permiso se comprueba con las mismas reglas que al guardar.
/// </summary>
internal sealed class AppsForm : Form
{
    private readonly AppsMonitor _apps;
    private readonly Func<string[], Task<int?>> _elevate;
    private readonly ListView _authorized = NewList(("Nombre", 220), ("Programa", 380));
    private readonly ListView _installed = NewList(("Programa", 220), ("Ruta", 380));
    private readonly TextBox _filter = new() { PlaceholderText = "Buscar programa…", Width = 260 };
    private readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Button _remove = new() { Text = "Quitar", AutoSize = true, Enabled = false };
    private readonly Button _add = new() { Text = "Autorizar seleccionado", AutoSize = true, Enabled = false };
    private readonly Button _browse = new() { Text = "Buscar .exe…", AutoSize = true };
    private IReadOnlyList<InstalledProgram> _programs = [];

    /// <param name="elevate">Ejecuta la orden del servicio con UAC; devuelve el código de salida o null si se canceló.</param>
    public AppsForm(AppsMonitor apps, Func<string[], Task<int?>> elevate)
    {
        _apps = apps;
        _elevate = elevate;
        Text = "Aplicaciones autorizadas · Pegasus Deck";
        Icon = Ui.AgentIcons.App;
        Width = 720;
        Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 7 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            Text = "Estas aplicaciones se pueden abrir desde el iPad. Añadir o quitar pide permiso de administrador " +
                   "(Windows lo confirmará). El iPad las verá en unos segundos, sin reiniciar nada.",
        });
        layout.Controls.Add(_authorized);
        layout.Controls.Add(Row(_remove));
        layout.Controls.Add(new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Text = "Añadir un programa instalado", Margin = new Padding(0, 12, 0, 4) });
        layout.Controls.Add(Stack(_filter, _installed));
        layout.Controls.Add(Row(_add, _browse));
        layout.Controls.Add(_status);
        Controls.Add(layout);

        _authorized.SelectedIndexChanged += (_, _) => _remove.Enabled = _authorized.SelectedItems.Count == 1;
        _installed.SelectedIndexChanged += (_, _) => _add.Enabled = _installed.SelectedItems.Count == 1;
        _installed.DoubleClick += async (_, _) => await AddSelectedAsync();
        _filter.TextChanged += (_, _) => FillInstalled();
        _remove.Click += async (_, _) => await RemoveSelectedAsync();
        _add.Click += async (_, _) => await AddSelectedAsync();
        _browse.Click += async (_, _) => await BrowseAsync();
        Load += async (_, _) =>
        {
            FillAuthorized();
            _status.Text = "Buscando programas instalados…";
            _programs = await Task.Run(() => InstalledPrograms.Scan().Concat(PackagedApps.Scan())
                .GroupBy(p => p.Aumid ?? p.ExePath, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
            FillInstalled();
            _status.Text = $"{_programs.Count} programas encontrados (menú Inicio y Microsoft Store). ¿No está? Usa «Buscar .exe…».";
        };
    }

    private static ListView NewList(params (string Title, int Width)[] columns)
    {
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
        foreach (var (title, width) in columns) list.Columns.Add(title, width);
        return list;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        row.Controls.AddRange(controls);
        return row;
    }

    private static TableLayoutPanel Stack(Control top, Control fill)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.Controls.Add(top);
        t.Controls.Add(fill);
        return t;
    }

    private void FillAuthorized()
    {
        _authorized.BeginUpdate();
        _authorized.Items.Clear();
        foreach (var a in _apps.Apps) _authorized.Items.Add(new ListViewItem([a.Name, a.ExePath]) { Tag = a });
        _authorized.EndUpdate();
        _remove.Enabled = false;
    }

    private void FillInstalled()
    {
        var q = _filter.Text.Trim();
        _installed.BeginUpdate();
        _installed.Items.Clear();
        foreach (var p in _programs.Where(p => q.Length == 0 || p.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
        {
            var already = _apps.Apps.Any(a => p.Aumid is not null ? string.Equals(a.Aumid, p.Aumid, StringComparison.OrdinalIgnoreCase)
                : string.Equals(a.ExePath, p.ExePath, StringComparison.OrdinalIgnoreCase));
            var label = p.Name + (p.Aumid is not null ? "  · Microsoft Store" : "") + (already ? "  (ya autorizada)" : "");
            _installed.Items.Add(new ListViewItem([label, p.ExePath]) { Tag = p, ForeColor = already ? SystemColors.GrayText : SystemColors.WindowText });
        }
        _installed.EndUpdate();
        _add.Enabled = false;
    }

    private async Task AddSelectedAsync()
    {
        if (_installed.SelectedItems.Count != 1 || _installed.SelectedItems[0].Tag is not InstalledProgram p) return;
        await AddAsync(p.ExePath, p.Name, p.Aumid);
    }

    private async Task BrowseAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Elige el programa que se podrá abrir desde el iPad",
            Filter = "Programas (*.exe)|*.exe",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await AddAsync(dialog.FileName, AppsEditor.SuggestName(dialog.FileName));
    }

    private async Task AddAsync(string exePath, string name, string? aumid = null)
    {
        // Comprobación previa con las mismas reglas que al guardar: el usuario ve el motivo sin pasar por UAC.
        var (entry, error) = AppsEditor.Prepare(_apps.Apps, exePath, name, allowUnprotectedPath: false, aumid);
        if (entry is null)
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var allowUnprotected = false;
        if (entry.Aumid is null && ExecutableSecurity.Problem(entry.ExePath) is { } risk)
        {
            var answer = MessageBox.Show(this,
                $"«{entry.Name}» está en una carpeta que puede modificar cualquier usuario de este PC ({risk}).\n\n" +
                "Si alguien sustituye ese programa, el iPad abriría lo que haya puesto en su lugar.\n\n" +
                "¿Autorizarlo igualmente? (Solo si confías en todos los usuarios de este PC.)",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            allowUnprotected = true;
        }
        List<string> args = ["apps", "add", $"--exe={entry.ExePath}", $"--name={entry.Name}"];
        if (allowUnprotected) args.Add("--allow-unprotected");
        if (entry.Aumid is not null) args.Add($"--aumid={entry.Aumid}");
        await RunAsync([.. args], $"«{entry.Name}» autorizada.", () => _apps.Apps.Any(a => string.Equals(a.ExePath, entry.ExePath, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task RemoveSelectedAsync()
    {
        if (_authorized.SelectedItems.Count != 1 || _authorized.SelectedItems[0].Tag is not AppEntry app) return;
        var answer = MessageBox.Show(this, $"¿Quitar «{app.Name}»? Ya no se podrá abrir desde el iPad.", Text,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        await RunAsync(["apps", "remove", app.Id], $"«{app.Name}» quitada.", () => _apps.Find(app.Id) is null);
    }

    /// <summary>Lanza la orden con UAC y espera a que el servicio envíe la lista nueva al agente.</summary>
    private async Task RunAsync(string[] args, string done, Func<bool> applied)
    {
        UseWaitCursor = true;
        _status.Text = "Esperando el permiso de Windows…";
        try
        {
            var code = await _elevate(args);
            if (code is null)
            {
                _status.Text = "Cancelado: no se ha cambiado nada.";
                return;
            }
            if (code != 0)
            {
                _status.Text = code == 3
                    ? "No se ha podido escribir en la carpeta de datos de Pegasus Deck."
                    : "El servicio ha rechazado el cambio (revisa que el programa siga existiendo).";
                return;
            }
            _status.Text = "Guardado. Aplicando…";
            for (var i = 0; i < 40 && !applied(); i++) await Task.Delay(250);
            FillAuthorized();
            FillInstalled();
            // No se afirma lo que no se ha comprobado: el agente debe haber recibido la lista nueva.
            _status.Text = applied() ? $"{done} El iPad ya la ve." : "Guardado, pero el servicio aún no lo ha aplicado (¿está en marcha?).";
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    /// <summary>Ejecuta el servicio con UAC (ventana oculta) y devuelve su código de salida; null si se cancela.</summary>
    public static async Task<int?> ElevateAsync(string serviceExe, IReadOnlyList<string> extraArgs, string[] args)
    {
        if (!File.Exists(serviceExe)) return 3;
        var psi = new ProcessStartInfo(serviceExe) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var a in args.Concat(extraArgs)) psi.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(psi);
            if (process is null) return null;
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Win32Exception)
        {
            return null; // el usuario canceló la elevación
        }
    }
}
