import { describe, expect, it } from 'vitest';
import { allOf, describe as describeCond, evaluate, type Condition } from '../../src/core/conditions';
import type { Facts } from '../../src/core/system';

const facts: Facts = {
  always: true,
  'pc.online': true,
  'service.ready': true,
  'obs.ready': true,
  'obs.recording': false,
  'obs.scene': 'Juego',
  'app.davinci.state': 'installed',
  'app.davinci.running': false,
};

describe('evaluador de condiciones', () => {
  it('siempre disponible', () => {
    expect(evaluate({ always: true }, {}).value).toBe(true);
  });

  it('AND: «Iniciar grabación» con todos los requisitos', () => {
    const c: Condition = { all: [{ fact: 'pc.online' }, { fact: 'service.ready' }, { fact: 'obs.ready' }, { not: { fact: 'obs.recording' } }] };
    expect(evaluate(c, facts)).toMatchObject({ value: true, missing: [] });
    const recording = evaluate(c, { ...facts, 'obs.recording': true });
    expect(recording.value).toBe(false);
    expect(recording.missing).toEqual(['Grabación no iniciada']);
    expect(recording.blockers).toEqual(['Grabación activa']);
  });

  it('enumera todos los requisitos que faltan', () => {
    const c: Condition = { all: [{ fact: 'pc.online' }, { fact: 'service.ready' }] };
    const r = evaluate(c, { 'pc.online': false, 'service.ready': false });
    expect(r.missing).toEqual(['PC online', 'Servicio conectado']);
    expect(r.blockers).toEqual(['PC no disponible', 'Servicio desconectado']);
  });

  it('OR: basta con una', () => {
    const c: Condition = { any: [{ fact: 'app.davinci.running' }, { fact: 'obs.ready' }] };
    expect(evaluate(c, facts).value).toBe(true);
    const none = evaluate(c, { ...facts, 'obs.ready': false });
    expect(none.value).toBe(false);
    expect(none.missing[0]).toMatch(/^Una de: \(DaVinci Resolve abierta o OBS conectado\)$/);
  });

  it('NOT sobre un grupo aplica De Morgan en los textos', () => {
    const c: Condition = { not: { any: [{ fact: 'pc.online' }, { fact: 'service.ready' }] } };
    expect(describeCond(c)).toBe('(PC no disponible y Servicio desconectado)');
    expect(evaluate(c, { 'pc.online': false, 'service.ready': false }).value).toBe(true);
    expect(evaluate(c, { 'pc.online': true, 'service.ready': false }).value).toBe(false);
  });

  it('desconocido nunca habilita: AND con un desconocido no es true', () => {
    const c: Condition = { all: [{ fact: 'pc.online' }, { fact: 'obs.ready' }] };
    const r = evaluate(c, { 'pc.online': true, 'obs.ready': null });
    expect(r.value).toBeNull();
    expect(r.unknown).toEqual(['OBS conectado']);
    // pero un requisito que falla seguro manda sobre el desconocido
    expect(evaluate(c, { 'pc.online': false, 'obs.ready': null }).value).toBe(false);
  });

  it('NOT de desconocido sigue siendo desconocido', () => {
    expect(evaluate({ not: { fact: 'obs.recording' } }, {}).value).toBeNull();
  });

  it('eq e in', () => {
    expect(evaluate({ fact: 'obs.scene', eq: 'Juego' }, facts).value).toBe(true);
    expect(evaluate({ not: { fact: 'obs.scene', eq: 'Juego' } }, facts).value).toBe(false);
    expect(evaluate({ fact: 'app.davinci.state', in: ['installed', 'running'] }, facts).value).toBe(true);
    expect(evaluate({ fact: 'app.davinci.state', in: ['ready'] }, facts).missing).toEqual(['DaVinci Resolve lista']);
    expect(evaluate({ fact: 'app.davinci.state', in: ['ready'] }, facts).blockers).toEqual(['DaVinci Resolve cerrada']);
  });

  it('apps sin datos con el PC apagado no se consideran abiertas', () => {
    const r = evaluate({ fact: 'app.chrome.running' }, { 'pc.online': false });
    expect(r.value).toBe(false);
  });

  it('allOf descarta "siempre" y aplana casos simples', () => {
    expect(allOf({ always: true }, { always: true })).toEqual({ always: true });
    expect(allOf({ always: true }, { fact: 'pc.online' })).toEqual({ fact: 'pc.online' });
    expect(allOf({ fact: 'a' }, { fact: 'b' })).toEqual({ all: [{ fact: 'a' }, { fact: 'b' }] });
  });
});
