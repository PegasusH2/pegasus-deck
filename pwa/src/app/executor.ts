// Adaptador de botones: disponibilidad → confirmación → motor de acciones →
// estado visual del botón y aviso con el resultado. Toda la lógica de ejecución
// está en actionEngine.ts.

import { getAction } from '../core/actions';
import { evaluate } from '../core/conditions';
import { buttonAvailability, buttonTimeoutMs, resolveButton, type DeckButton } from '../core/deck';
import { executeAction, type ActionOutcome } from './actionEngine';
import { useApp } from './store';


export function explainUnavailable(button: DeckButton): void {
  const { facts, toast } = useApp.getState();
  const av = buttonAvailability(button, facts);
  const reasons = av.missing.length ? av.missing : av.unknown.map((u) => `${u} (estado desconocido)`);
  toast('info', `«${button.name}» no está disponible`, `Falta: ${reasons.join(' · ')}`);
}

export async function runButton(original: DeckButton): Promise<void> {
  const app = useApp.getState();
  const button = resolveButton(original, app.facts); // «Encender PC» / «Apagar PC» según el estado
  if (app.buttonState[button.id] === 'running') return; // doble toque

  const def = getAction(button.action.type);
  if (!def) {
    failButton(button.id, 'Acción desconocida');
    return;
  }
  if (!buttonAvailability(button, app.facts).enabled) {
    explainUnavailable(button);
    return;
  }

  const confirm = button.confirm ?? (def.destructive ? { text: `¿Seguro que quieres ejecutar «${button.name}»?`, verb: 'Ejecutar' } : null);
  if (confirm && !(await app.askConfirm(button.name, confirm.text, confirm.verb))) return;

  app.setButtonState(button.id, 'running');
  const outcome = await executeAction(button.action.type, button.action.params, {
    timeoutMs: buttonTimeoutMs(button),
    retry: button.retry,
    label: button.name,
  });
  report(button, outcome);
}

/**
 * Triple toque en un hueco de app de General (ADR-024): cierra todas las ventanas de esa app
 * como con su X, tras confirmar cuántas son. El PC exige además el segundo paso.
 */
export async function closeAppWindows(button: DeckButton): Promise<void> {
  const app = useApp.getState();
  const appId = String(button.action.params.appId);
  const info = app.snapshot?.apps.find((a) => a.id === appId);
  const name = info?.name ?? button.name.replace(/^Abrir /, '');
  const closer: DeckButton = { ...button, name: `Cerrar ${name}`.slice(0, 24), feedback: { success: null }, confirm: null };
  if (app.buttonState[button.id] === 'running') return;

  const check = evaluate(getAction('app.closeAll')!.requires({ appId }), app.facts);
  if (check.value !== true) {
    const why = check.value === false ? (check.missing.join(' · ') || 'no está abierta') : 'estado desconocido';
    failButton(button.id, `No se puede cerrar: ${why}`);
    return;
  }
  const n = info?.windows;
  if (n === 0) {
    failButton(button.id, 'Sin ventanas abiertas');
    return;
  }
  const what = n === undefined ? 'todas las ventanas' : n === 1 ? 'la ventana' : `las ${n} ventanas`;
  const ok = await app.askConfirm(`Cerrar ${name}`,
    `¿Cerrar ${what} de ${name}? Se cierran como con la X: si hay cambios sin guardar, la app lo preguntará en el PC.`, 'Cerrar ventanas');
  if (!ok) return;

  app.setButtonState(button.id, 'running');
  const outcome = await executeAction('app.closeAll', { appId }, { label: closer.name });
  report({ ...closer, id: button.id }, outcome);
}

/**
 * Resultado de una acción (ADR-029): sin avisos emergentes. Bien → nada; mal → el propio botón tiembla y
 * muestra el motivo unos segundos. Solo si el PC ya no reconoce el iPad se abre el emparejamiento.
 */
function report(button: DeckButton | null, outcome: ActionOutcome): void {
  const s = useApp.getState();
  if (outcome.status === 'completed') {
    if (button) s.setButtonState(button.id, null);
    return;
  }
  if (outcome.status === 'failed' && outcome.code === 'AUTH_REQUIRED') s.setPairingOpen(true);
  if (button) failButton(button.id, outcome.message);
}

const NOTE_MS = 4000;

/** Marca el botón como fallido con un motivo breve (se borra solo). */
function failButton(id: string, message: string): void {
  useApp.getState().setButtonState(id, 'failed', message);
  setTimeout(() => {
    if (useApp.getState().buttonState[id] === 'failed') useApp.getState().setButtonState(id, null);
  }, NOTE_MS);
}
