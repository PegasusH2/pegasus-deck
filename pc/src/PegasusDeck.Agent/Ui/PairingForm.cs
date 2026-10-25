using System.Text.Json;
using PegasusDeck.Core.Contract;

namespace PegasusDeck.Agent.Ui;

/// <summary>
/// Código de emparejamiento en una ventana propia (sin consola). El código lo genera la
/// orden local del servicio con permiso de administrador (UAC, ADR-011/013), que lo deja
/// en un archivo temporal del usuario; el agente lo lee, lo borra y lo muestra. La ventana
/// solo dice «emparejado» cuando el servicio lo confirma por el pipe.
/// </summary>
internal sealed class PairingForm : Form
{
    private static readonly Color Bg = Color.FromArgb(0x05, 0x09, 0x13);
    private static readonly Color Surface = Color.FromArgb(0x13, 0x1e, 0x38);
    private static readonly Color Text1 = Color.FromArgb(0xf1, 0xf5, 0xf9);
    private static readonly Color Text2 = Color.FromArgb(0x94, 0xa3, 0xb8);
    private static readonly Color Ok = Color.FromArgb(0x22, 0xc5, 0x5e);
    private static readonly Color Warn = Color.FromArgb(0xf5, 0x9e, 0x0b);
    private static readonly Color Info = Color.FromArgb(0x3b, 0x82, 0xf6);

    private readonly Func<string[], Task<int?>> _elevate;
    private readonly Label _code = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Text1, Font = new Font("Segoe UI Semibold", 44f) };
    private readonly Label _title = new() { AutoSize = true, ForeColor = Text1, Font = new Font("Segoe UI Semibold", 15f), Text = "Emparejar iPad" };
    private readonly Label _hint = new() { AutoSize = false, Height = 44, Dock = DockStyle.Top, ForeColor = Text2, Font = new Font("Segoe UI", 10.5f) };
    private readonly Label _expiry = new() { AutoSize = true, ForeColor = Text2, Font = new Font("Segoe UI", 9.5f) };
    private readonly Panel _bar = new() { Height = 4, Dock = DockStyle.Bottom, BackColor = Surface };
    private readonly Panel _barFill = new() { Height = 4, Dock = DockStyle.Left, BackColor = Info };
    private readonly Button _again = Flat("Generar otro código", Info);
    private readonly Button _close = Flat("Cerrar", Surface);
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 250 };
    private DateTimeOffset _expiresAt;
    private TimeSpan _lifetime = TimeSpan.FromMinutes(2);
    private bool _paired;

    public PairingForm(Func<string[], Task<int?>> elevate)
    {
        _elevate = elevate;
        Text = "Emparejar iPad · Pegasus Deck";
        Icon = AgentIcons.App;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(440, 400);
        BackColor = Bg;
        Font = new Font("Segoe UI", 10f);

        var logo = new PictureBox { Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Image = new Icon(AgentIcons.App, 48, 48).ToBitmap() };
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(20, 16, 20, 0), BackColor = Bg };
        _title.Margin = new Padding(10, 12, 0, 0);
        header.Controls.AddRange([logo, _title]);

        var card = new Panel { Dock = DockStyle.Top, Height = 132, BackColor = Surface, Margin = new Padding(20) };
        card.Controls.Add(_code);
        card.Controls.Add(_bar);
        _bar.Controls.Add(_barFill);
        var cardWrap = new Panel { Dock = DockStyle.Top, Height = 156, Padding = new Padding(20, 12, 20, 12), BackColor = Bg };
        cardWrap.Controls.Add(card);

        var info = new Panel { Dock = DockStyle.Top, Height = 84, Padding = new Padding(22, 4, 22, 0), BackColor = Bg };
        info.Controls.Add(_expiry);
        info.Controls.Add(_hint);
        _expiry.Dock = DockStyle.Bottom;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 12, 16, 12), BackColor = Bg };
        buttons.Controls.AddRange([_close, _again]);

        Controls.Add(buttons);
        Controls.Add(info);
        Controls.Add(cardWrap);
        Controls.Add(header);

        _close.Click += (_, _) => Close();
        _again.Click += async (_, _) => await GenerateAsync();
        _tick.Tick += (_, _) => UpdateCountdown();
        Shown += async (_, _) => await GenerateAsync();
        FormClosed += (_, _) => _tick.Dispose();
    }

    private static Button Flat(string text, Color back)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, MinimumSize = new Size(0, 38), Padding = new Padding(12, 0, 12, 0), Margin = new Padding(8, 0, 0, 0),
            FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10f), Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    /// <summary>El servicio confirma que un iPad se ha emparejado con el código.</summary>
    public void ShowPaired(string deviceName)
    {
        _paired = true;
        _tick.Stop();
        _code.Text = "✓";
        _code.ForeColor = Ok;
        _barFill.Width = _bar.Width;
        _barFill.BackColor = Ok;
        _hint.Text = $"«{deviceName}» emparejado. Ya puedes usar Pegasus Deck en el iPad.";
        _expiry.Text = "";
        _again.Visible = false;
        _close.BackColor = Ok;
        Activate();
    }

    private async Task GenerateAsync()
    {
        _paired = false;
        _tick.Stop();
        _again.Enabled = false;
        _code.ForeColor = Text2;
        _code.Font = new Font("Segoe UI Semibold", 20f);
        _code.Text = "Esperando permiso…";
        _hint.Text = "Windows pedirá permiso de administrador para generar el código.";
        _expiry.Text = "";
        _barFill.Width = 0;

        var file = Path.GetTempFileName(); // vacío y solo accesible por este usuario
        try
        {
            var exit = await _elevate(["pair", $"--code-file={file}"]);
            if (exit is null)
            {
                Fail("Cancelado: no se ha generado ningún código.");
                return;
            }
            if (exit != 0 || ReadCode(file) is not { } result)
            {
                Fail("No se ha podido generar el código. ¿Está en marcha el servicio de Pegasus Deck?");
                return;
            }
            _expiresAt = result.ExpiresAt;
            _lifetime = result.ExpiresAt - DateTimeOffset.Now;
            _code.Font = new Font("Segoe UI Semibold", 44f);
            _code.ForeColor = Text1;
            _code.Text = $"{result.Code[..3]} {result.Code[3..]}";
            _hint.Text = "En el iPad abre Pegasus Deck, pulsa «Emparejar» y escribe este código.";
            _barFill.BackColor = Info;
            UpdateCountdown();
            _tick.Start();
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
            _again.Enabled = true;
        }
    }

    private void Fail(string message)
    {
        _code.Text = "—";
        _hint.Text = message;
    }

    private void UpdateCountdown()
    {
        if (_paired) return;
        var left = _expiresAt - DateTimeOffset.Now;
        if (left <= TimeSpan.Zero)
        {
            _tick.Stop();
            _code.ForeColor = Text2;
            _expiry.Text = "Código caducado. Genera otro si aún no has emparejado.";
            _expiry.ForeColor = Warn;
            _barFill.Width = 0;
            return;
        }
        _expiry.ForeColor = Text2;
        _expiry.Text = $"Caduca en {(int)left.TotalMinutes}:{left.Seconds:00} · un solo uso · 5 intentos";
        _barFill.Width = (int)(_bar.Width * Math.Clamp(left / _lifetime, 0, 1));
    }

    private sealed record CodeFile(string Code, DateTimeOffset ExpiresAt);

    private static CodeFile? ReadCode(string path)
    {
        try
        {
            var r = JsonSerializer.Deserialize<CodeFile>(File.ReadAllText(path), JsonDefaults.Options);
            return r is { Code.Length: 6 } && r.Code.All(char.IsAsciiDigit) ? r : null;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return null;
        }
    }
}
