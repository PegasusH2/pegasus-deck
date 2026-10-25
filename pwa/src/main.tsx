import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { connection } from './app/connection';
import { loadConfig, startAutosave } from './app/persistence';
import { useApp } from './app/store';
import './index.css';

async function boot() {
  const { warnings, notices, ...config } = await loadConfig();
  useApp.getState().hydrate(config);
  startAutosave();
  for (const w of warnings) useApp.getState().toast('error', 'Configuración restaurada', w);
  for (const n of notices) useApp.getState().toast('info', 'Configuración actualizada', n);
  connection.start();
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);

void boot();
