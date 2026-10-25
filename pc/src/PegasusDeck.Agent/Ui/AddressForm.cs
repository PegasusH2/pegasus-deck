using System.Text.Json.Nodes;
using PegasusDeck.Core.Network;

namespace PegasusDeck.Agent.Ui;

/// <summary>
/// «Dirección IP del PC…» (ADR-034): muestra la IP en la que escucha el servicio y las que tiene ahora el PC, y
/// permite elegir o escribir otra. El cambio lo hace la orden local del servicio con permiso de administrador
/// (UAC); aquí solo se valida para avisar antes y se explica el resultado.
/// </summary>
internal sealed class AddressForm : Form
{
    private static readonly Color Bg = Color.FromArgb(0x05, 0x09, 0x13);
    private static readonly Color Surface = Color.FromArgb(0x13, 0x1e, 0x38);
    private static readonly Color Text1 = Color.FromArgb(0xf1, 0xf5, 0xf9);
    private static readonly Color Text2 = Color.FromArgb(0x94, 0xa3, 0xb8);
    private static readonly Color Ok = Color.FromArgb(0x22, 0xc5, 0x5e);
    private static readonly Color Warn = Color.FromArgb(0xf5, 0x9e, 0x0b);
    private static readonly Color Bad = Color.FromArgb(0xef, 0x44, 0x44);
    private static readonly Color Info = Color.FromArgb(0x3b, 0x82, 0xf6);

    private readonly Func<string[], Task<int?>> _elevate;
    private readonly string? _settingsFile;
    private readonly Label _current = Caption("", 11f, Text1, bold: true);
    private readonly Label _currentState = Caption("", 9.5f, Text2);
    private readonly FlowLayoutPanel _choices = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = Bg, Margin = new Padding(0, 4, 0, 4) };
    private readonly TextBox _ip = new() { Width = 220, Font = new Font("Segoe UI", 12f), BackColor = Surface, ForeColor = Text1, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "192.168.1.23" };
    private readonly Label _status = new() { AutoSize = false, Width = 470, Height = 92, ForeColor = Text2, Font = new Font("Segoe UI", 10f), Margin = new Padding(0, 8, 0, 0) };
    private readonly Button _apply = Flat("Aplicar", Info);
    private readonly Button _close = Flat("Cerrar", Surface);

    public AddressForm(string? settingsFile, Func<string[], Task<int?>> elevate)
    {
        _settingsFile = settingsFile;
        _elevate = elevate;
        Text = "Dirección IP del PC · Pegasus Deck";
        Icon = AgentIcons.App;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Bg;
        Font = new Font("Segoe UI", 10f);

        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(22, 18, 22, 14), BackColor = Bg };
        var logo = new PictureBox { Size = new Size(40, 40), SizeMode = PictureBoxSizeMode.Zoom, Image = new Icon(AgentIcons.App, 48, 48).ToBitmap() };
        var title = Caption("Dirección IP del PC", 15f, Text1, bold: true);
        title.Margin = new Padding(8, 6, 0, 0);
        var header = new FlowLayoutPanel { AutoSize = true, BackColor = Bg, Margin = new Padding(0, 0, 0, 10) };
        header.Controls.AddRange([logo, title]);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Width = 470, BackColor = Bg, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.AddRange([_close, _apply]);

        layout.Controls.AddRange([
            header,
            Caption("El iPad se conecta a:", 9.5f, Text2), _current, _currentState,
            Gap(),
            Caption("IP de este PC ahora (pulsa una para elegirla):", 9.5f, Text2), _choices,
            Caption("O escríbela:", 9.5f, Text2), _ip,
            _status, buttons,
        ]);
        Controls.Add(layout);

        AcceptButton = _apply;
        _close.Click += (_, _) => Close();
        _apply.Click += async (_, _) => await ApplyAsync();
        _ip.TextChanged += (_, _) => CheckInput();
        Shown += (_, _) => Reload(null);
    }

    /// <summary>IP configurada en el servicio instalado (null si no se puede leer).</summary>
    public static string? ConfiguredAddress(string? settingsFile)
    {
        try
        {
            if (settingsFile is null || !File.Exists(settingsFile)) return null;
            return (JsonNode.Parse(File.ReadAllText(settingsFile))?["Pegasus"]?["ListenAddresses"] as JsonArray)?.FirstOrDefault()?.GetValue<string>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public static int ConfiguredPort(string? settingsFile)
    {
        try
        {
            return settingsFile is not null && File.Exists(settingsFile)
                ? JsonNode.Parse(File.ReadAllText(settingsFile))?["Pegasus"]?["Port"]?.GetValue<int>() ?? 8443
                : 8443;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return 8443;
        }
    }

    private void Reload(string? done)
    {
        var configured = ConfiguredAddress(_settingsFile);
        var local = LanAddresses.Current();
        var port = ConfiguredPort(_settingsFile);
        _current.Text = configured is null ? "— (no se encuentra el servicio instalado)" : $"https://{configured}:{port}";
        if (configured is null)
        {
            _currentState.Text = "Instala Pegasus Deck con install.ps1 para poder cambiar la IP desde aquí.";
            _currentState.ForeColor = Warn;
            _apply.Enabled = false;
        }
        else if (local.Any(a => a.Address.ToString() == configured))
        {
            _currentState.Text = "✓ Es una IP de este PC.";
            _currentState.ForeColor = Ok;
        }
        else
        {
            _currentState.Text = "✕ Este PC ya no tiene esa IP: el iPad no puede conectar. Elige la actual.";
            _currentState.ForeColor = Bad;
        }

        _choices.Controls.Clear();
        foreach (var a in local)
        {
            var chip = Flat(a.HasGateway ? $"{a.Address}  ·  {a.Interface}" : $"{a.Address}  ·  {a.Interface} (sin router)", Surface);
            chip.Margin = new Padding(0, 0, 8, 6);
            var ip = a.Address.ToString();
            chip.Click += (_, _) => _ip.Text = ip;
            _choices.Controls.Add(chip);
        }
        if (local.Count == 0) _choices.Controls.Add(Caption("Ninguna: comprueba que el cable o la Wi-Fi están conectados.", 9.5f, Warn));

        if (done is not null)
        {
            _status.ForeColor = Ok;
            _status.Text = done;
        }
        else if (configured is not null && string.IsNullOrEmpty(_ip.Text))
        {
            _ip.Text = local.FirstOrDefault(a => a.HasGateway)?.Address.ToString() ?? configured;
        }
    }

    private bool CheckInput()
    {
        var (ip, problem) = LanAddresses.Check(_ip.Text, LanAddresses.Current());
        if (ip is null)
        {
            _status.ForeColor = Warn;
            _status.Text = problem;
            return false;
        }
        _status.ForeColor = Text2;
        _status.Text = ip.ToString() == ConfiguredAddress(_settingsFile)
            ? "Es la IP que ya usa el servicio. Pulsa Aplicar para reiniciarlo con ella."
            : "Al aplicar, el iPad tendrá que abrir la dirección nueva, añadirla a la pantalla de inicio y emparejar otra vez.";
        return true;
    }

    private async Task ApplyAsync()
    {
        if (!CheckInput()) return;
        var ip = _ip.Text.Trim();
        _apply.Enabled = false;
        _status.ForeColor = Text2;
        _status.Text = "Windows pedirá permiso de administrador. Aplicando y reiniciando el servicio…";
        try
        {
            var resultFile = Path.GetTempFileName(); // vacío y solo accesible por este usuario
            int? exit;
            string? detail;
            try
            {
                exit = await _elevate(["address", "set", ip, $"--ca-root={CaRoot()}", $"--result-file={resultFile}"]);
                detail = ReadResult(resultFile);
            }
            finally
            {
                try { File.Delete(resultFile); } catch (IOException) { }
            }
            var port = ConfiguredPort(_settingsFile);
            switch (exit)
            {
                case null:
                    _status.ForeColor = Warn;
                    _status.Text = "Cancelado: no se ha cambiado nada.";
                    break;
                case 0:
                    Reload($"✓ Listo. En el iPad abre en Safari https://{ip}:{port}, añádela a la pantalla de inicio (Compartir → Añadir a pantalla de inicio) y empareja con «Emparejar iPad…». Borra el icono antiguo.");
                    break;
                default:
                    _status.ForeColor = Bad;
                    _status.Text = detail is not null ? $"No se ha cambiado. {detail}" : exit switch
                    {
                        1 => $"No se ha cambiado: {ip} no es válida para este PC.",
                        3 => "No se ha cambiado: hace falta permiso de administrador.",
                        4 => "No se ha cambiado: no se encuentra la CA de mkcert (rootCA.pem y rootCA-key.pem) con la que se creó el certificado del iPad. Ejecuta «mkcert -CAROOT» para ver dónde debería estar.",
                        5 => $"El servicio no arranca con {ip}; se ha dejado todo como estaba.",
                        _ => $"No se ha podido cambiar (código {exit}).",
                    };
                    break;
            }
        }
        finally
        {
            _apply.Enabled = ConfiguredAddress(_settingsFile) is not null;
        }
    }

    /// <summary>Mensaje que deja la orden elevada (null si no hay).</summary>
    private static string? ReadResult(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            return text.Length == 0 ? null : JsonNode.Parse(text)?["message"]?.GetValue<string>();
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>CA de mkcert del usuario (también si quedó en la carpeta virtual de una app empaquetada).</summary>
    private static string CaRoot() => PegasusDeck.Core.Security.MkcertRoot.Default();

    private static Label Caption(string text, float size, Color color, bool bold = false) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = color,
        Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size), Margin = new Padding(0, 2, 0, 2),
    };

    private static Panel Gap() => new Panel { Height = 8, Width = 10, BackColor = Bg };

    private static Button Flat(string text, Color back)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, MinimumSize = new Size(0, 36), Padding = new Padding(10, 0, 10, 0), Margin = new Padding(8, 0, 0, 0),
            FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10f), Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}
