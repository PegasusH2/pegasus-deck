using System.Runtime.InteropServices;
using PegasusDeck.Core.Resolve;

namespace PegasusDeck.Agent.Windows;

/// <summary>
/// Pulsa un atajo de la lista cerrada (ADR-028/031). Recibe la tecla en formato Qt (el de Resolve) y la
/// traduce a teclas de Windows con la distribución de teclado de la ventana destino (en un teclado español
/// «=» es Shift+0) y con su código físico (scan code): sin él, Qt interpreta mal Shift+número. Nunca texto libre.
/// </summary>
internal static class Keyboard
{
    private const ushort VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;

    private static readonly Dictionary<int, ushort> Special = new()
    {
        [0x20] = 0x20, // espacio
        [0x01000000] = 0x1B, [0x01000003] = 0x08, [0x01000004] = 0x0D, [0x01000005] = 0x0D, [0x01000007] = 0x2E,
        [0x01000010] = 0x24, [0x01000011] = 0x23, [0x01000012] = 0x25, [0x01000013] = 0x26, [0x01000014] = 0x27,
        [0x01000015] = 0x28, [0x01000016] = 0x21, [0x01000017] = 0x22,
    };

    private static readonly HashSet<ushort> Extended = [0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2E];

    /// <summary>null si se ha enviado; si no, el motivo.</summary>
    public static string? Send(int qtKey, IntPtr layout)
    {
        var ctrl = (qtKey & ResolveCommands.Ctrl) != 0;
        var shift = (qtKey & ResolveCommands.Shift) != 0;
        var alt = (qtKey & ResolveCommands.Alt) != 0;
        var code = qtKey & 0x01FFFFFF;

        ushort vk;
        if (Special.TryGetValue(code, out var special)) vk = special;
        else if (code is >= 0x01000030 and <= 0x01000052) vk = (ushort)(0x70 + code - 0x01000030); // F1…F35
        else if (code is >= 'A' and <= 'Z' or >= '0' and <= '9') vk = (ushort)code;
        else if (code is > 0x20 and < 0x7F)
        {
            // Signos: qué tecla física los produce en la distribución activa (y con qué modificadores).
            var scan = NativeMethods.VkKeyScanEx((ushort)code, layout);
            if (scan == -1) return $"tu teclado no tiene la tecla «{(char)code}»";
            vk = (ushort)(scan & 0xFF);
            shift |= (scan & 0x100) != 0;
            ctrl |= (scan & 0x200) != 0;
            alt |= (scan & 0x400) != 0;
        }
        else
        {
            return $"tecla no admitida (0x{code:X})";
        }

        var mods = new List<ushort>();
        if (ctrl) mods.Add(VkControl);
        if (shift) mods.Add(VkShift);
        if (alt) mods.Add(VkMenu);

        var inputs = new List<NativeMethods.KeyInput>();
        foreach (var m in mods) inputs.Add(Key(m, up: false, layout));
        inputs.Add(Key(vk, up: false, layout));
        inputs.Add(Key(vk, up: true, layout));
        for (var i = mods.Count - 1; i >= 0; i--) inputs.Add(Key(mods[i], up: true, layout));

        var array = inputs.ToArray();
        return NativeMethods.SendInput((uint)array.Length, array, Marshal.SizeOf<NativeMethods.KeyInput>()) == array.Length
            ? null
            : "Windows no ha aceptado las teclas";
    }

    private static NativeMethods.KeyInput Key(ushort vk, bool up, IntPtr layout) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Vk = vk,
        Scan = (ushort)NativeMethods.MapVirtualKeyEx(vk, 0 /* MAPVK_VK_TO_VSC */, layout),
        Flags = (up ? NativeMethods.KeyEventKeyUp : 0) | (Extended.Contains(vk) ? NativeMethods.KeyEventExtended : 0),
    };
}
