import { z } from 'zod';

// Sin encendido ni apagado desde el iPad (ADR-032): solo el nombre y la IP informativa del PC.
// Los campos antiguos de Wake-on-LAN que hubiera guardados se descartan al leer (zod los ignora).
export const pcSettingsSchema = z.object({
  name: z.string().trim().min(1, 'Pon un nombre').max(40),
  /** IP o nombre del PC en la LAN (informativo: la API está en el mismo origen que la PWA). */
  host: z.string().trim().max(253),
});

export type PcSettings = z.infer<typeof pcSettingsSchema>;

export const DEFAULT_PC_SETTINGS: PcSettings = {
  name: 'PC',
  host: '',
};
