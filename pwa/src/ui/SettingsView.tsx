import { useState } from 'react';
import { ChevronLeft } from 'lucide-react';
import { useApp } from '../app/store';
import { forgetDevice } from '../app/pairing';
import { DEFAULT_DECK } from '../core/deck';
import { pcSettingsSchema, type PcSettings } from '../core/settings';
import { Button, Field, inputClass, Section } from './kit';

type Form = Record<keyof PcSettings, string>;

function toForm(s: PcSettings): Form {
  return Object.fromEntries(Object.entries(s).map(([k, v]) => [k, String(v)])) as Form;
}

function fromForm(f: Form): unknown {
  return { ...f };
}

export function SettingsView() {
  const settings = useApp((s) => s.settings);
  const device = useApp((s) => s.device);
  const setSettings = useApp((s) => s.setSettings);
  const setView = useApp((s) => s.setView);
  const setPairingOpen = useApp((s) => s.setPairingOpen);
  const toast = useApp((s) => s.toast);
  const askConfirm = useApp((s) => s.askConfirm);
  const [form, setForm] = useState<Form>(() => toForm(settings));
  const [errors, setErrors] = useState<Partial<Record<keyof PcSettings, string>>>({});

  const set = (k: keyof PcSettings) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
    setForm({ ...form, [k]: e.target.value });

  const save = () => {
    const parsed = pcSettingsSchema.safeParse(fromForm(form));
    if (!parsed.success) {
      const errs: Partial<Record<keyof PcSettings, string>> = {};
      for (const issue of parsed.error.issues) errs[issue.path[0] as keyof PcSettings] ??= issue.message;
      setErrors(errs);
      toast('error', 'Revisa los campos marcados');
      return false;
    }
    setErrors({});
    setSettings(parsed.data);
    setForm(toForm(parsed.data));
    toast('success', 'Configuración guardada');
    return true;
  };

  const resetDeck = async () => {
    if (await askConfirm('Restablecer panel', 'Se sustituirán los botones y páginas por los de fábrica.', 'Restablecer')) {
      useApp.setState({ deck: DEFAULT_DECK, activePageId: DEFAULT_DECK.pages[0].id });
      toast('success', 'Panel restablecido');
    }
  };

  const forget = async () => {
    if (await askConfirm('Olvidar emparejamiento', 'Este iPad dejará de poder controlar el PC hasta que lo vuelvas a emparejar. Para bloquearlo del todo, revócalo también en el PC.', 'Olvidar')) {
      forgetDevice();
      toast('info', 'Emparejamiento olvidado');
    }
  };

  return (
    <div className="mx-auto w-full max-w-3xl space-y-5 overflow-y-auto pb-16">
      <div className="flex items-center justify-between">
        <Button variant="ghost" onClick={() => setView('deck')}><ChevronLeft size={20} /> Panel</Button>
        <h2 className="text-xl font-bold text-fg">Configuración</h2>
        <Button variant="primary" data-testid="settings-save" onClick={save}>Guardar</Button>
      </div>

      <Section title="Emparejamiento">
        {device ? (
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-sm text-fg-2" data-testid="pairing-status">
              Emparejado como <b className="text-fg">{device.deviceName}</b> desde el {new Date(device.pairedAt).toLocaleDateString()}.
            </p>
            <div className="flex gap-2">
              <Button onClick={() => setPairingOpen(true)}>Volver a emparejar</Button>
              <Button variant="danger" onClick={() => void forget()}>Olvidar</Button>
            </div>
          </div>
        ) : (
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-sm text-fg-2" data-testid="pairing-status">Este iPad no está emparejado. Hace falta para controlar el PC.</p>
            <Button variant="primary" onClick={() => setPairingOpen(true)}>Emparejar</Button>
          </div>
        )}
      </Section>

      <Section title="Equipo">
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Nombre" error={errors.name}><input className={inputClass} data-testid="field-name" value={form.name} onChange={set('name')} /></Field>
          <Field label="IP del PC en la red local" error={errors.host} hint="Informativo. Usa una reserva DHCP en el router.">
            <input className={inputClass} value={form.host} onChange={set('host')} placeholder="192.168.1.100" inputMode="decimal" />
          </Field>
        </div>
      </Section>

      <Section title="Panel">
        <p className="text-sm text-fg-2">El editor visual de botones llega en la fase 4. Mientras tanto puedes volver al panel de fábrica.</p>
        <Button variant="danger" onClick={() => void resetDeck()}>Restablecer panel por defecto</Button>
      </Section>
    </div>
  );
}
