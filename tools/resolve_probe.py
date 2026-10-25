"""Prueba de viabilidad DaVinci Resolve (ADR pendiente).

Fase 1 (por defecto, SOLO LECTURA): ¿acepta Resolve control externo? edición, versión, página,
proyecto, clip actual y nodos. No cambia nada.

Fase 2 (--write, solo en un proyecto de PRUEBA): mide cuántos SetCDL por segundo aguanta en el
nodo 1 del clip actual y lo deja neutro al acabar (Slope 1, Offset 0, Power 1, Saturación 1).
Borra la corrección CDL de ese nodo: no usar sobre un trabajo real.
"""
import os
import sys
import time

RESOLVE_DIR = r"C:\Program Files\Blackmagic Design\DaVinci Resolve"
MODULES = r"C:\ProgramData\Blackmagic Design\DaVinci Resolve\Support\Developer\Scripting\Modules"
os.environ.setdefault("RESOLVE_SCRIPT_API", r"C:\ProgramData\Blackmagic Design\DaVinci Resolve\Support\Developer\Scripting")
os.environ.setdefault("RESOLVE_SCRIPT_LIB", os.path.join(RESOLVE_DIR, "fusionscript.dll"))
sys.path.append(MODULES)


def main() -> int:
    try:
        import DaVinciResolveScript as dvr  # noqa: N813
    except ImportError as e:
        print(f"[X] No se puede cargar el módulo de scripting de Resolve: {e}")
        return 2
    resolve = dvr.scriptapp("Resolve")
    if resolve is None:
        print("[X] Resolve no acepta control externo (cerrado, edición gratuita o")
        print("    Preferencias > Sistema > General > 'Scripting externo' desactivado).")
        return 1
    print(f"[OK] Conectado: {resolve.GetProductName()} {resolve.GetVersionString()}")
    print(f"     Página actual: {resolve.GetCurrentPage()}")
    project = resolve.GetProjectManager().GetCurrentProject()
    if not project:
        print("[!] No hay proyecto abierto.")
        return 0
    print(f"     Proyecto: {project.GetName()}")
    timeline = project.GetCurrentTimeline()
    if not timeline:
        print("[!] No hay línea de tiempo abierta.")
        return 0
    item = timeline.GetCurrentVideoItem()
    if not item:
        print("[!] No hay clip seleccionado bajo el cursor.")
        return 0
    graph = item.GetNodeGraph() if hasattr(item, "GetNodeGraph") else None
    nodes = graph.GetNumNodes() if graph else item.GetNumNodes()
    print(f"     Clip actual: {item.GetName()} · nodos: {nodes}")

    if "--write" not in sys.argv:
        print("\nFase 1 terminada (no se ha cambiado nada).")
        return 0

    print("\nFase 2: midiendo SetCDL en el nodo 1 (proyecto de PRUEBA)…")
    n = 60
    start = time.perf_counter()
    ok = 0
    for i in range(n):
        g = 1.0 + 0.3 * ((i % 20) / 20)
        ok += bool(item.SetCDL({"NodeIndex": "1", "Slope": f"{g} {g} {g}", "Offset": "0 0 0", "Power": "1 1 1", "Saturation": "1"}))
    elapsed = time.perf_counter() - start
    item.SetCDL({"NodeIndex": "1", "Slope": "1 1 1", "Offset": "0 0 0", "Power": "1 1 1", "Saturation": "1"})
    print(f"[OK] {ok}/{n} cambios aceptados en {elapsed:.2f} s → {n / elapsed:.1f} cambios/s ({1000 * elapsed / n:.0f} ms cada uno)")
    print("     Nodo 1 dejado neutro.")
    if graph and hasattr(graph, "SetNodeEnabled"):
        print(f"     Activar/desactivar nodo disponible: {graph.SetNodeEnabled(1, True)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
