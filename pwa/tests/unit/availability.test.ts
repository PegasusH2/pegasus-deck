// Disponibilidad de los botones por fases del sistema: apagado →
// enlace conectando → Windows sin sesión → sesión → OBS abierto → OBS cerrado.
import { describe, expect, it } from 'vitest';
import { buttonAvailability, DEFAULT_DECK, deckSchema, type DeckButton } from '../../src/core/deck';
import type { LinkState } from '../../src/core/linkMachine';
import type { PcState } from '../../src/core/pcMachine';
import { buildFacts, type ServiceSnapshot } from '../../src/core/system';
import { link, machine, snapshot } from './fixtures';

const button = (id: string): DeckButton => DEFAULT_DECK.buttons.find((b) => b.id === id)!;

function enabledSet(state: PcState, snap: ServiceSnapshot | null, linkState: LinkState = 'READY'): Set<string> {
  const facts = buildFacts({ pc: machine(state), link: link(linkState), snapshot: snap });
  return new Set(DEFAULT_DECK.buttons.filter((b) => buttonAvailability(b, facts).enabled).map((b) => b.id));
}

const LOCAL = ['check', 'settings'];

describe('botones según el estado del sistema', () => {
  it('la configuración por defecto es válida y todas las páginas son 6×3', () => {
    const r = deckSchema.safeParse(DEFAULT_DECK);
    expect(r.success, JSON.stringify(r.error?.issues)).toBe(true);
    for (const page of DEFAULT_DECK.pages) expect(page.slots).toHaveLength(18);
  });

  it('PC apagado: solo comprobar y configuración (Pegasus Deck no lo enciende, ADR-032)', () => {
    expect(enabledSet('PC_OFFLINE', null, 'DISCONNECTED')).toEqual(new Set(LOCAL));
  });

  it('PC apagado: los botones dependientes explican el motivo', () => {
    const facts = buildFacts({ pc: machine('PC_OFFLINE'), link: link('DISCONNECTED'), snapshot: null });
    const av = buttonAvailability(button('rec-start'), facts);
    expect(av.enabled).toBe(false);
    expect(av.missing).toContain('PC online');
    expect(av.blockers[0]).toBe('PC no disponible');
  });

  it('estado desconocido al abrir: nada que dependa del PC se habilita', () => {
    expect(enabledSet('UNKNOWN', null, 'DISCONNECTED')).toEqual(new Set(LOCAL));
  });

  it('PC online pero enlace conectando: los botones del PC esperan (estado desconocido)', () => {
    const facts = buildFacts({ pc: machine('PC_ONLINE'), link: link('CONNECTING'), snapshot: snapshot() });
    expect(enabledSet('PC_ONLINE', snapshot(), 'CONNECTING')).toEqual(new Set(LOCAL));
    expect(buttonAvailability(button('open-chrome'), facts).missing).toContain('Servicio conectado');
  });

  it('sin emparejar: nada del PC se habilita aunque responda', () => {
    expect(enabledSet('PC_ONLINE', snapshot(), 'AUTH_REQUIRED')).toEqual(new Set(LOCAL));
  });

  it('Windows iniciado sin sesión: aún no se abren programas', () => {
    const set = enabledSet('PC_ONLINE', snapshot({ session: false }));
    for (const id of ['open-chrome', 'lock', 'rec-start']) expect(set.has(id)).toBe(false);
  });

  it('sesión iniciada: botones de Windows sí, OBS no', () => {
    const set = enabledSet('PC_ONLINE', snapshot());
    for (const id of ['open-chrome', 'open-davinci', 'open-explorer', 'open-obs', 'lock']) expect(set.has(id)).toBe(true);
    for (const id of ['rec-start', 'stream-start', 'mic-mute', 'scene-chat']) expect(set.has(id)).toBe(false);
  });

  it('OBS conectado: controles de OBS según su estado', () => {
    const set = enabledSet('PC_ONLINE', snapshot({ obs: { state: 'ready' }, apps: { obs: 'ready' } }));
    for (const id of ['rec-start', 'stream-start', 'mic-mute', 'scene-chat']) expect(set.has(id)).toBe(true);
    expect(set.has('open-obs')).toBe(false); // ya está abierto
    expect(set.has('scene-game')).toBe(false); // ya es la escena activa
    expect(set.has('rec-stop')).toBe(false); // no se está grabando
  });

  it('perder OBS solo deshabilita lo que depende de OBS', () => {
    const withObs = enabledSet('PC_ONLINE', snapshot({ obs: { state: 'ready' }, apps: { obs: 'ready' } }));
    const withoutObs = enabledSet('PC_ONLINE', snapshot());
    for (const id of ['open-chrome', 'lock']) {
      expect(withObs.has(id)).toBe(true);
      expect(withoutObs.has(id)).toBe(true);
    }
    expect(withoutObs.has('rec-start')).toBe(false);
  });

  it('grabando: se puede detener y pausar, no volver a iniciar', () => {
    const set = enabledSet('PC_ONLINE', snapshot({ obs: { state: 'ready', recording: true }, apps: { obs: 'ready' } }));
    expect(set.has('rec-stop')).toBe(true);
    expect(set.has('rec-pause')).toBe(true);
    expect(set.has('rec-start')).toBe(false);
  });

  it('una escena o fuente que no existe deshabilita el botón con motivo', () => {
    const facts = buildFacts({ pc: machine('PC_ONLINE'), link: link('READY'), snapshot: snapshot({ obs: { state: 'ready', scenes: ['Juego'], inputs: [] } }) });
    const scene = buttonAvailability(button('scene-chat'), facts);
    expect(scene.enabled).toBe(false);
    expect(scene.missing).toContain('Escena «Charla» existe');
    expect(buttonAvailability(button('mic-mute'), facts).missing).toContain('Fuente «Mic/Aux» existe');
  });

  it('una app fuera de la lista blanca del PC no se puede abrir', () => {
    const facts = buildFacts({ pc: machine('PC_ONLINE'), link: link('READY'), snapshot: { ...snapshot(), apps: [] } });
    const av = buttonAvailability(button('open-chrome'), facts);
    expect(av.enabled).toBe(false);
    expect(av.missing).toContain('Chrome autorizada en el PC');
  });

  it('OBS con contraseña incorrecta no habilita sus botones', () => {
    const set = enabledSet('PC_ONLINE', snapshot({ obs: { state: 'auth_error' }, apps: { obs: 'running' } }));
    expect(set.has('rec-start')).toBe(false);
  });

  it('acción desconocida queda deshabilitada', () => {
    const facts = buildFacts({ pc: machine('PC_ONLINE'), link: link('READY'), snapshot: snapshot() });
    const av = buttonAvailability({ ...button('lock'), action: { type: 'system.exec', params: {} } }, facts);
    expect(av.enabled).toBe(false);
    expect(av.missing[0]).toMatch(/Acción desconocida/);
  });
});
