import { useApp } from './app/store';
import { DeckGrid, PageTabs } from './ui/DeckGrid';
import { Header } from './ui/Header';
import { NotPaired, useNeedsPairing } from './ui/NotPaired';
import { AppPickerSheet } from './ui/AppPicker';
import { ConfirmDialog, Toasts } from './ui/Overlays';
import { PairingDialog } from './ui/PairingDialog';
import { SectionPickerSheet } from './ui/SectionPicker';
import { SettingsView } from './ui/SettingsView';
import { StatusBanner } from './ui/StatusBanner';
import { StatusSheet } from './ui/StatusSheet';

export function App() {
  const loaded = useApp((s) => s.loaded);
  const view = useApp((s) => s.view);
  const needsPairing = useNeedsPairing();

  return (
    <div className="flex h-dvh flex-col overflow-hidden bg-bg text-fg">
      <main className="mx-auto mt-1 flex min-h-0 w-full max-w-6xl flex-1 flex-col gap-3 px-4 pb-4 pt-[max(0.75rem,env(safe-area-inset-top))]">
        {!loaded ? null : view === 'settings' ? <SettingsView /> : (
          <>
            <Header />
            {needsPairing ? <NotPaired /> : (
              <>
                <StatusBanner />
                <PageTabs />
                <DeckGrid />
              </>
            )}
          </>
        )}
      </main>
      <StatusSheet />
      <PairingDialog />
      <AppPickerSheet />
      <SectionPickerSheet />
      <ConfirmDialog />
      <Toasts />
    </div>
  );
}
