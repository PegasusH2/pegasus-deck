// Órdenes de DaVinci Resolve (ADR-028/031): lista cerrada id → orden interna de Resolve + atajo por defecto.
// El agente usa el atajo que el usuario tenga en SU Resolve (preset de teclado activo); el de aquí solo se
// usa si no se puede leer. Mismos ids que contracts/resolve-commands.json.
// Teclas en formato Qt (el que guarda Resolve): modificadores en los bits altos + código de tecla.

namespace PegasusDeck.Core.Resolve;

public sealed record ResolveCommand(string Id, string ResolveName, int DefaultKey);

public static class ResolveCommands
{
    public const int Shift = 0x02000000, Ctrl = 0x04000000, Alt = 0x08000000;
    public const int KeySpace = 0x20, KeyDelete = 0x01000007, KeyHome = 0x01000010, KeyUp = 0x01000013, KeyDown = 0x01000015, KeyF9 = 0x01000038, KeyF10 = 0x01000039;

    private static ResolveCommand C(string id, string name, int key) => new(id, name, key);

    public static readonly IReadOnlyDictionary<string, ResolveCommand> All = new[]
    {
        // Páginas
        C("page.media", "workspacePrimaryWorkspaceMedia", Shift | '2'),
        C("page.cut", "workspacePrimaryWorkspaceFirstCut", Shift | '3'),
        C("page.edit", "workspacePrimaryWorkspaceEdit", Shift | '4'),
        C("page.fusion", "workspacePrimaryWorkspaceFusion", Shift | '5'),
        C("page.color", "workspacePrimaryWorkspaceColor", Shift | '6'),
        C("page.fairlight", "workspacePrimaryWorkspaceAudio", Shift | '7'),
        C("page.deliver", "workspacePrimaryWorkspaceDeliver", Shift | '8'),
        // Edición
        C("edit.blade", "editBladeRazor", Ctrl | 'B'),
        C("edit.delete", "editDelete", KeyDelete),
        C("edit.markIn", "markIn", 'I'),
        C("edit.markOut", "markOut", 'O'),
        C("edit.snapping", "editSnapping", 'N'),
        C("edit.insert", "editInsertOverwriteActionInsert", KeyF9),
        C("edit.overwrite", "editInsertOverwriteActionOverwrite", KeyF10),
        C("edit.undo", "editUndo", Ctrl | 'Z'),
        C("edit.redo", "editRedo", Ctrl | Shift | 'Z'),
        // Color
        C("color.addSerialNode", "nodesAddSerial", Alt | 'S'),
        C("color.addParallelNode", "nodesAddParallel", Alt | 'P'),
        C("color.addLayerNode", "nodesAddLayer", Alt | 'L'),
        C("color.toggleNode", "nodesToggleCurrent", Ctrl | 'D'),
        C("color.toggleAllNodes", "nodesToggleAll", Alt | 'D'),
        C("color.bypassAll", "viewBypassColorGrades", Shift | 'D'),
        C("color.grabStill", "viewStillsStillGrab", Ctrl | Alt | 'G'),
        C("color.splitScreen", "viewSplitScreenModeOnOffToggle", Ctrl | Alt | 'W'),
        C("color.highlight", "viewHighlightHighlight", Shift | 'H'),
        C("color.copyPrevious", "sessionGradeFromOneClipPrior", Shift | '='),
        C("color.resetGrade", "sessionBaseMemoryReset", Ctrl | KeyHome),
        // Entrega
        C("project.export", "fileExportProject", Ctrl | 'E'),
        // Photo
        C("photo.like", "PhotoPanel..photoTagGoodTake", 'M'),
        C("photo.dislike", "PhotoPanel..photoTagRejected", 'X'),
        // Globales
        C("edit.marker", "markMarkerAdd", 'M'),
        C("clip.previous", "controlClipPrev", KeyUp),
        C("clip.next", "controlClipNext", KeyDown),
        C("playback.toggle", "controlPlayToggle", KeySpace),
        C("project.save", "fileSaveProject", Ctrl | 'S'),
    }.ToDictionary(c => c.Id, StringComparer.Ordinal);

    public static bool IsKnown(string id) => All.ContainsKey(id);

    /// <summary>Texto legible de una tecla Qt (para mensajes).</summary>
    public static string Describe(int key)
    {
        var parts = new List<string>();
        if ((key & Ctrl) != 0) parts.Add("Ctrl");
        if ((key & Shift) != 0) parts.Add("Shift");
        if ((key & Alt) != 0) parts.Add("Alt");
        var k = key & 0x01FFFFFF;
        parts.Add(k switch
        {
            KeySpace => "Espacio",
            KeyDelete => "Supr",
            KeyHome => "Inicio",
            KeyUp => "Flecha arriba",
            KeyDown => "Flecha abajo",
            >= 0x01000030 and <= 0x01000052 => $"F{k - 0x01000030 + 1}",
            > 0x20 and < 0x7F => ((char)k).ToString(),
            _ => $"0x{k:X}",
        });
        return string.Join('+', parts);
    }
}
