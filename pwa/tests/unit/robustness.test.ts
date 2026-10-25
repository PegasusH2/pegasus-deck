// Dirección de capas, confirmaciones tempranas y hechos de apps sin sesión.
import fs from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { onOperationResult, trackOperation } from '../../src/app/operations';
import { buttonAvailability, DEFAULT_DECK } from '../../src/core/deck';
import { buildFacts, factValue } from '../../src/core/system';
import { link, machine, snapshot } from './fixtures';

const SRC = path.resolve(__dirname, '../../src');

function importsOf(dir: string): { file: string; spec: string }[] {
  return fs.readdirSync(path.join(SRC, dir), { recursive: true, withFileTypes: true })
    .filter((d) => d.isFile() && /\.tsx?$/.test(d.name))
    .flatMap((d) => {
      const file = path.join(d.parentPath, d.name);
      const text = fs.readFileSync(file, 'utf8');
      return [...text.matchAll(/from '([^']+)'/g)].map((m) => ({ file: path.relative(SRC, file), spec: m[1] }));
    });
}

describe('dirección de capas ui → app → core', () => {
  it('core no importa de app ni de ui', () => {
    expect(importsOf('core').filter((i) => /\/(app|ui)\//.test(`${i.spec}/`) || /^\.\.\/(app|ui)/.test(i.spec))).toEqual([]);
  });
  it('app no importa de ui', () => {
    expect(importsOf('app').filter((i) => /^\.\.\/ui/.test(i.spec))).toEqual([]);
  });
});

describe('operaciones', () => {
  it('una confirmación que llega antes que la respuesta HTTP no se pierde', async () => {
    onOperationResult({ operationId: 'aaaaaaaaaaaaaaaa', status: 'completed', message: 'Escena: Juego.' });
    await expect(trackOperation('aaaaaaaaaaaaaaaa', 'Escena', 5000)).resolves.toEqual({ status: 'completed', message: 'Escena: Juego.' });
  });

  it('una confirmación posterior resuelve la espera', async () => {
    const p = trackOperation('bbbbbbbbbbbbbbbb', 'Grabar', 5000);
    onOperationResult({ operationId: 'bbbbbbbbbbbbbbbb', status: 'failed', code: 'TIMEOUT', message: 'OBS no confirmó.' });
    await expect(p).resolves.toEqual({ status: 'failed', code: 'TIMEOUT', message: 'OBS no confirmó.' });
  });
});

describe('apps sin sesión de Windows', () => {
  const facts = buildFacts({ pc: machine('PC_ONLINE'), link: link('READY'), snapshot: { ...snapshot({ session: false }), apps: [] } });

  it('su estado es desconocido (no "no autorizada"), pero no están abiertas', () => {
    expect(factValue(facts, 'app.chrome.state')).toBeNull();
    expect(factValue(facts, 'app.chrome.running')).toBe(false);
  });

  it('el motivo mostrado es la falta de sesión', () => {
    const av = buttonAvailability(DEFAULT_DECK.buttons.find((b) => b.id === 'open-chrome')!, facts);
    expect(av.enabled).toBe(false);
    expect(av.missing).toContain('Sesión de Windows iniciada');
    expect(av.missing.join(' ')).not.toMatch(/autorizada/);
  });

  it('un hecho llamado como una propiedad de Object no se cuela', () => {
    expect(factValue(facts, 'constructor')).toBeNull();
    expect(factValue(facts, 'toString')).toBeNull();
  });
});
