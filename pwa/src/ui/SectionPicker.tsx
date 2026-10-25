// Lista de secciones de una página con secciones (DaVinci, ADR-031). La elegida se recuerda en el iPad.

import { Check } from 'lucide-react';
import { useApp } from '../app/store';
import { iconFor } from './icons';
import { Sheet } from './kit';

export function SectionPickerSheet() {
  const pageId = useApp((s) => s.sectionPickerPage);
  const close = useApp((s) => s.setSectionPickerPage);
  const page = useApp((s) => s.deck.pages.find((p) => p.id === pageId));
  const active = useApp((s) => (pageId ? s.activeSections[pageId] : undefined));
  if (!page?.sections) return null;
  const current = active ?? page.sections[0].id;

  return (
    <Sheet title={`${page.name} · Sección`} onClose={() => close(null)}>
      <ul className="space-y-2" aria-label="Secciones">
        {page.sections.map((section) => {
          const Icon = iconFor(section.icon);
          const selected = section.id === current;
          return (
            <li key={section.id}>
              <button type="button" aria-pressed={selected}
                onClick={() => { useApp.getState().setActiveSection(page.id, section.id); close(null); }}
                className={`flex min-h-[52px] w-full items-center gap-3 rounded-md border px-3 text-left transition-colors duration-press ${
                  selected ? 'border-info bg-info/15' : 'border-line/10 hover:bg-line/5'}`}>
                <Icon size={22} className="shrink-0 text-fg-2" aria-hidden />
                <span className="flex-1 font-medium text-fg">{section.name}</span>
                {selected && <Check size={20} className="text-info" aria-hidden />}
              </button>
            </li>
          );
        })}
      </ul>
    </Sheet>
  );
}
