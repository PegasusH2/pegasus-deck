// Agente de sesión de Pegasus Deck: se inicia con la sesión del usuario (fase 5:
// tarea programada "al iniciar sesión") y hace lo que el servicio no puede desde la
// sesión 0: abrir y enfocar apps, bloquear, audio y OBS.

using System.ComponentModel;
using System.Diagnostics;
using PegasusDeck.Agent.Apps;
using PegasusDeck.Agent.Audio;
using PegasusDeck.Agent.Media;
using PegasusDeck.Agent.Ui;

namespace PegasusDeck.Agent
{
    internal static class AgentProgram
    {
        [STAThread]
        public static int Main(string[] args)
        {
#if DEBUG
            if (args.Contains("--test-window"))
            {
                // Solo pruebas: una ventana normal (fuera de la pantalla) que se cierra con su X / WM_CLOSE.
                ApplicationConfiguration.Initialize();
                Application.Run(new Form { Text = "Pegasus Deck · ventana de pruebas", StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), Size = new Size(200, 120) });
                return 0;
            }
#endif
            var options = AgentOptions.Load(args);

            // Una instancia por sesión.
            using var single = new Mutex(true, $@"Local\PegasusDeck.Agent.{options.PipeName}", out var first);
            if (!first) return 0;

            using var cts = new CancellationTokenSource();
            using var runtime = new AgentRuntime(options, new CoreAudioController(), media: new SmtcMediaController());
            var running = Task.Run(() => runtime.RunAsync(cts.Token));

            if (options.Headless)
            {
                // Solo pruebas: sin bandeja; termina cuando se cierra el proceso.
                try { running.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
                return 0;
            }

            ApplicationConfiguration.Initialize();
            using (new TrayIcon(runtime, options))
            {
                Application.Run();
            }
            cts.Cancel();
            try { running.Wait(TimeSpan.FromSeconds(3)); }
            catch (AggregateException) { }
            return 0;
        }
    }

    /// <summary>Icono de bandeja: estado, emparejar, aplicaciones autorizadas (con elevación), configurar OBS e IP del PC.</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _status;
        private readonly SynchronizationContext _ui;
        private readonly AgentOptions _options;
        private readonly AgentRuntime _runtime;
        private AppsForm? _appsForm;
        private PairingForm? _pairingForm;
        private AddressForm? _addressForm;
        private readonly System.Windows.Forms.Timer _addressCheck = new() { Interval = 60_000 };
        private string? _warnedAddress;

        public TrayIcon(AgentRuntime runtime, AgentOptions options)
        {
            _options = options;
            _runtime = runtime;
            _menu = new ContextMenuStrip();
            _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            _status = new ToolStripMenuItem("Iniciando…") { Enabled = false };
            _menu.Items.Add(_status);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Emparejar iPad…", null, (_, _) => Pair());
            _menu.Items.Add("Aplicaciones autorizadas…", null, (_, _) => ShowApps());
            _menu.Items.Add("Configurar OBS…", null, (_, _) => ConfigureObs());
            _menu.Items.Add("Dirección IP del PC…", null, (_, _) => ShowAddress());
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Salir", null, (_, _) => Application.Exit());
            _icon = new NotifyIcon { Icon = AgentIcons.Tray(AgentIcons.Status.Connecting), Text = "Pegasus Deck", ContextMenuStrip = _menu, Visible = true };
            _icon.DoubleClick += (_, _) => ShowApps();
            runtime.Connection.StatusChanged += text => _ui.Post(_ =>
            {
                _status.Text = text.Length > 60 ? text[..60] : text;
                _icon.Text = "Pegasus Deck · " + (text.Length > 40 ? text[..40] : text);
                var old = _icon.Icon;
                _icon.Icon = AgentIcons.Tray(text.StartsWith("Conectado", StringComparison.Ordinal) ? AgentIcons.Status.Connected : AgentIcons.Status.Disconnected);
                old?.Dispose();
            }, null);
            // Si el router cambia la IP del PC, el iPad deja de conectar: se avisa y se ofrece cambiarla (ADR-034).
            _icon.BalloonTipClicked += (_, _) => { if (_warnedAddress is not null) ShowAddress(); };
            _addressCheck.Tick += (_, _) => CheckAddress();
            _addressCheck.Start();
            _ui.Post(_ => CheckAddress(), null);
            runtime.Connection.Paired += name => _ui.Post(_ =>
            {
                if (_pairingForm is { IsDisposed: false } form) form.ShowPaired(name);
                else _icon.ShowBalloonTip(4000, "Pegasus Deck", $"«{name}» emparejado.", ToolTipIcon.Info);
            }, null);
        }

        /// <summary>El código se genera con privilegios de administrador (ADR-013): Windows pedirá permiso.</summary>
        private void Pair()
        {
            // El servicio instalado junto al agente (nunca una ruta configurable: se eleva con UAC);
            // solo en desarrollo (Debug + --insecure-dev), el de --trusted-service y su carpeta de datos.
            var service = _options.ElevatedServicePath;
            if (!File.Exists(service))
            {
                MessageBox.Show($"No se encuentra el servicio de Pegasus Deck:{Environment.NewLine}{service}", "Emparejar iPad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_pairingForm is { IsDisposed: false })
            {
                _pairingForm.Activate();
                return;
            }
            _pairingForm = new PairingForm(args => AppsForm.ElevateAsync(service, _options.ElevatedServiceArgs, args));
            _pairingForm.FormClosed += (_, _) => { _pairingForm.Dispose(); _pairingForm = null; };
            _pairingForm.Show();
        }

        /// <summary>Ver, añadir y quitar apps autorizadas (ADR-023). Una sola ventana a la vez.</summary>
        private void ShowApps()
        {
            if (_appsForm is { IsDisposed: false })
            {
                _appsForm.Activate();
                return;
            }
            _appsForm = new AppsForm(_runtime.Apps, args => AppsForm.ElevateAsync(_options.ElevatedServicePath, _options.ElevatedServiceArgs, args));
            _appsForm.FormClosed += (_, _) => { _appsForm.Dispose(); _appsForm = null; };
            _appsForm.Show();
        }

        private string ServiceSettingsFile => Path.Combine(Path.GetDirectoryName(_options.ElevatedServicePath) ?? AppContext.BaseDirectory, "appsettings.json");

        /// <summary>Ver y cambiar la IP de la LAN en la que escucha el servicio (ADR-034). Una sola ventana a la vez.</summary>
        private void ShowAddress()
        {
            if (_addressForm is { IsDisposed: false })
            {
                _addressForm.Activate();
                return;
            }
            _addressForm = new AddressForm(ServiceSettingsFile, args => AppsForm.ElevateAsync(_options.ElevatedServicePath, _options.ElevatedServiceArgs, args));
            _addressForm.FormClosed += (_, _) => { _addressForm.Dispose(); _addressForm = null; };
            _addressForm.Show();
        }

        /// <summary>Avisa una vez por cada IP configurada que el PC ya no tiene.</summary>
        private void CheckAddress()
        {
            var configured = AddressForm.ConfiguredAddress(ServiceSettingsFile);
            if (configured is null) return;
            var local = PegasusDeck.Core.Network.LanAddresses.Current();
            if (local.Count == 0 || local.Any(a => a.Address.ToString() == configured))
            {
                _warnedAddress = null;
                return;
            }
            if (_warnedAddress == configured) return;
            _warnedAddress = configured;
            _icon.ShowBalloonTip(10_000, "Pegasus Deck: la IP del PC ha cambiado",
                $"El iPad busca el PC en {configured}, pero ahora tiene {local[0].Address}. Pulsa aquí para cambiarla.", ToolTipIcon.Warning);
        }

        private void ConfigureObs()
        {
            using var form = new ObsSettingsForm(_options.ObsPort);
            if (form.ShowDialog() != DialogResult.OK) return;
            _options.ObsPort = form.Port;
            if (form.PasswordChanged) _options.ObsPassword = form.Password;
            _options.Save();
        }

        public void Dispose()
        {
            _addressCheck.Dispose();
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
        }
    }

    /// <summary>Puerto y contraseña de obs-websocket (OBS → Herramientas → Ajustes del servidor WebSocket).</summary>
    internal sealed class ObsSettingsForm : Form
    {
        private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Width = 120 };
        private readonly TextBox _password = new() { UseSystemPasswordChar = true, Width = 240, PlaceholderText = "(sin cambios)" };

        public ObsSettingsForm(int port)
        {
            Text = "Pegasus Deck · OBS";
            Icon = AgentIcons.App;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _port.Value = port;
            var ok = new Button { Text = "Guardar", DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel };
            AcceptButton = ok;
            CancelButton = cancel;
            var layout = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(12) };
            layout.Controls.Add(new Label { Text = "Puerto", AutoSize = true, Anchor = AnchorStyles.Left });
            layout.Controls.Add(_port);
            layout.Controls.Add(new Label { Text = "Contraseña", AutoSize = true, Anchor = AnchorStyles.Left });
            layout.Controls.Add(_password);
            layout.Controls.Add(new Label { Text = "Se guarda cifrada para tu usuario (DPAPI).", AutoSize = true });
            layout.SetColumnSpan(layout.Controls[^1], 2);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(buttons);
            layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout);
        }

        public int Port => (int)_port.Value;

        public bool PasswordChanged => _password.Text.Length > 0;

        public string Password => _password.Text;
    }
}
