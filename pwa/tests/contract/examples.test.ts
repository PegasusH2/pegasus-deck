// Los ejemplos de contracts/ deben cumplir el esquema JSON y los analizadores
// Zod de la PWA deben opinar exactamente lo mismo.
import { describe, expect, it } from 'vitest';
import type { ZodTypeAny } from 'zod';
import {
  actionRequestSchema,
  actionResponseSchema,
  clientMessageSchema,
  healthSchema,
  pairRequestSchema,
  pairResponseSchema,
  serverMessageSchema,
  snapshotSchema,
} from '../../src/core/contract';
import { errorsOf, examples, index, validator } from './contract';

const ZOD: Record<string, ZodTypeAny> = {
  health: healthSchema,
  'pair.request': pairRequestSchema,
  'pair.response': pairResponseSchema,
  snapshot: snapshotSchema,
  'action.request': actionRequestSchema,
  'action.response': actionResponseSchema,
  'events.client': clientMessageSchema,
  'events.server': serverMessageSchema,
};

describe('contrato: ejemplos', () => {
  it('cada carpeta de ejemplos tiene analizador en la PWA', () => {
    expect(Object.keys(index).sort()).toEqual(Object.keys(ZOD).sort());
  });

  for (const [target, ref] of Object.entries(index)) {
    describe(target, () => {
      const validate = validator(ref);
      for (const [name, value] of examples(target, 'valid')) {
        it(`válido: ${name}`, () => {
          expect(validate(value), errorsOf(validate)).toBe(true);
          const z = ZOD[target].safeParse(value);
          expect(z.success, JSON.stringify(z.error?.issues)).toBe(true);
        });
      }
      for (const [name, value] of examples(target, 'invalid')) {
        it(`inválido: ${name}`, () => {
          expect(validate(value)).toBe(false);
          expect(ZOD[target].safeParse(value).success).toBe(false);
        });
      }
    });
  }
});
