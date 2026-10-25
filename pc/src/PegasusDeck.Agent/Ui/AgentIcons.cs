using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PegasusDeck.Agent.Ui;

/// <summary>Icono de Pegasus Deck (el mismo logotipo que la PWA) y su versión con punto de estado para la bandeja.</summary>
internal static partial class AgentIcons
{
    public enum Status { Connected, Connecting, Disconnected }

    private static Icon Load(int size)
    {
        using var stream = typeof(AgentIcons).Assembly.GetManifestResourceStream("PegasusDeck.Agent.pegasus.ico")
            ?? throw new InvalidOperationException("Falta el icono incrustado.");
        return new Icon(stream, size, size);
    }

    /// <summary>Icono de las ventanas del agente.</summary>
    public static Icon App { get; } = Load(32);

    /// <summary>Icono de la bandeja con un punto de estado (verde conectado, ámbar conectando, rojo sin conexión).</summary>
    public static Icon Tray(Status status)
    {
        var size = SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var logo = Load(size)) g.DrawIcon(logo, new Rectangle(0, 0, size, size));
            var d = Math.Max(6, size * 7 / 16);
            var dot = new Rectangle(size - d, size - d, d - 1, d - 1);
            var color = status switch
            {
                Status.Connected => Color.FromArgb(0x22, 0xc5, 0x5e),
                Status.Connecting => Color.FromArgb(0xf5, 0x9e, 0x0b),
                _ => Color.FromArgb(0xef, 0x44, 0x44),
            };
            using var ring = new Pen(Color.FromArgb(0x05, 0x09, 0x13), Math.Max(1.5f, size / 12f));
            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, dot);
            g.DrawEllipse(ring, dot);
        }
        var handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone(); // copia propia: el handle de GetHicon se libera aquí
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr handle);
}
