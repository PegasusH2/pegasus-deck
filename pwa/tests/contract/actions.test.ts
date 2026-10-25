// La lista blanca de la PWA coincide con contracts/actions.json, y los códigos de
// error con el esquema común. (El lado .NET tiene la prueba equivalente.)
import fs from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { listActions, validateParams, type ParamKind } from '../../src/core/actions';
import { ERROR_CODES } from '../../src/core/contract';
import { CONTRACTS } from './contract';

type Spec = { params: Record<string, 'id' | 'name' | 'percent' | 'resolveCommand'>; completion: 'response' | 'event'; timeoutMs: number; destructive?: boolean; valid: unknown[]; invalid: unknown[] };
const raw = JSON.parse(fs.readFileSync(path.join(CONTRACTS, 'actions.json'), 'utf8')) as Record<string, Spec | string>;
const specs = Object.fromEntries(Object.entries(raw).filter(([k]) => !k.startsWith('$'))) as Record<string, Spec>;

const KIND: Record<ParamKind, string> = { app: 'id', page: 'id', output: 'id', obsScene: 'name', obsInput: 'name', text: 'text', percent: 'percent', resolveCommand: 'resolveCommand' };

describe('contrato: lista blanca de acciones', () => {
  const service = listActions().filter((a) => a.executor === 'service');

  it('mismos tipos que contracts/actions.json', () => {
    expect(service.map((a) => a.type).sort()).toEqual(Object.keys(specs).sort());
  });

  for (const def of service) {
    describe(def.type, () => {
      const spec = specs[def.type];
      it('mismos parámetros, tipos y modo de confirmación', () => {
        expect(Object.fromEntries(def.params.map((p) => [p.key, KIND[p.kind]]))).toEqual(spec.params);
        expect(def.completion).toBe(spec.completion);
        expect(def.timeoutMs).toBe(spec.timeoutMs);
        expect(def.destructive).toBe(spec.destructive === true);
      });
      it('acepta los válidos y rechaza los inválidos', () => {
        for (const v of spec.valid) expect(validateParams(def.type, v).ok, JSON.stringify(v)).toBe(true);
        for (const v of spec.invalid) expect(validateParams(def.type, v).ok, JSON.stringify(v)).toBe(false);
      });
    });
  }

  it('los códigos de error de la PWA son los del esquema común', () => {
    const common = JSON.parse(fs.readFileSync(path.join(CONTRACTS, 'schemas', 'common.schema.json'), 'utf8'));
    expect([...ERROR_CODES].sort()).toEqual([...common.$defs.errorCode.enum].sort());
  });
});

describe('contrato: órdenes de DaVinci Resolve (ADR-028/031)', () => {
  it('la PWA tiene exactamente las de contracts/resolve-commands.json', async () => {
    const { RESOLVE_COMMANDS } = await import('../../src/core/resolve');
    const json = JSON.parse(fs.readFileSync(path.join(CONTRACTS, 'resolve-commands.json'), 'utf8')) as Record<string, string>;
    const ids = Object.keys(json).filter((k) => !k.startsWith('$'));
    expect(Object.keys(RESOLVE_COMMANDS).sort()).toEqual(ids.sort());
  });
});
