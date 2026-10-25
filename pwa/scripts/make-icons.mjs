// Genera los iconos PNG de la PWA a partir de public/icon.svg (usa WebKit de Playwright).
import fs from 'node:fs';
import path from 'node:path';
import { webkit } from '@playwright/test';

const root = path.resolve(import.meta.dirname, '..');
const svg = fs.readFileSync(path.join(root, 'public/icon.svg'), 'utf8');
const targets = [
  ['icon-192.png', 192, true],
  ['icon-512.png', 512, true],
  // iOS redondea las esquinas por su cuenta: icono a sangre, sin transparencia.
  ['apple-touch-icon.png', 180, false],
];

const browser = await webkit.launch();
for (const [file, size, rounded] of targets) {
  const page = await browser.newPage({ viewport: { width: size, height: size } });
  const body = rounded ? svg : svg.replace('rx="112"', 'rx="0"');
  await page.setContent(`<html><body style="margin:0;background:transparent">${body.replace('<svg ', `<svg width="${size}" height="${size}" `)}</body></html>`);
  await page.screenshot({ path: path.join(root, 'public', file), omitBackground: rounded });
  await page.close();
  console.log(`public/${file}`);
}
await browser.close();
