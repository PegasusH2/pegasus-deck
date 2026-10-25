// Carga los esquemas y ejemplos de contracts/ para las pruebas de contrato.
import fs from 'node:fs';
import path from 'node:path';
import Ajv2020 from 'ajv/dist/2020';
import type { ValidateFunction } from 'ajv';

export const CONTRACTS = path.resolve(__dirname, '../../../contracts');
const SCHEMAS = path.join(CONTRACTS, 'schemas');
const EXAMPLES = path.join(CONTRACTS, 'examples');
const BASE = 'https://pegasus-deck.local/contracts/';

const ajv = new Ajv2020({ allErrors: true, strict: true });
for (const file of fs.readdirSync(SCHEMAS)) {
  ajv.addSchema(JSON.parse(fs.readFileSync(path.join(SCHEMAS, file), 'utf8')));
}

export function validator(ref: string): ValidateFunction {
  const v = ajv.getSchema(BASE + ref);
  if (!v) throw new Error(`Esquema no encontrado: ${ref}`);
  return v;
}

export const index: Record<string, string> = JSON.parse(fs.readFileSync(path.join(EXAMPLES, 'index.json'), 'utf8'));

export function examples(target: string, kind: 'valid' | 'invalid'): [string, unknown][] {
  const dir = path.join(EXAMPLES, target, kind);
  return fs.readdirSync(dir).map((f) => [f, JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8'))]);
}

export function errorsOf(v: ValidateFunction): string {
  return ajv.errorsText(v.errors);
}
