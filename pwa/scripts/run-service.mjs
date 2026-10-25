// Arranca el servicio .NET y el agente de sesión para las E2E de integración.
// - Datos limpios y una lista blanca de pruebas (la app autorizada es el propio agente
//   sin ventana: no abre nada visible ni toca el audio real).
// - Servicio y agente se verifican mutuamente por la ruta de sus ejecutables (sin "*").
// Uso: node scripts/run-service.mjs   (configuración por variables, ver playwright.service.config.ts)
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';

const { SERVICE_EXE, AGENT_EXE, DATA_DIR, PIPE_NAME, DUMMY_PIPE, SERVICE_ARGS } = process.env;

fs.rmSync(DATA_DIR, { recursive: true, force: true });
fs.mkdirSync(DATA_DIR, { recursive: true });
// Ejecutable propio para la app de pruebas (copia del agente, sin ventana): si fuera el mismo
// ejecutable que el agente de sesión, este contaría como "la app ya abierta".
const TEST_APP = path.join(path.dirname(AGENT_EXE), 'PegasusDeck.TestApp.exe');
fs.copyFileSync(AGENT_EXE, TEST_APP);
fs.writeFileSync(path.join(DATA_DIR, 'apps.json'), JSON.stringify([
  // allowUnprotectedPath: la copia está en bin (carpeta del usuario); en producción no se permitiría sin decirlo expresamente.
  { id: 'dummy', name: 'App de pruebas', exePath: TEST_APP, args: ['--headless', `--pipe=${DUMMY_PIPE}`], readyLevel: 'process', alwaysLaunch: false, allowUnprotectedPath: true },
]));

const service = spawn(SERVICE_EXE, [
  `--Pegasus:DataDir=${DATA_DIR}`,
  `--Pegasus:AgentPipeName=${PIPE_NAME}`,
  `--Pegasus:TrustedAgentPath=${AGENT_EXE}`,
  ...SERVICE_ARGS.split(' ').filter(Boolean),
], { stdio: 'inherit' });

let agent = null;
const startAgent = () => {
  // --insecure-dev (solo Debug): el servicio de pruebas no corre en la sesión 0. La ruta del servicio sí se comprueba.
  // Aislado del OBS real del usuario: configuración propia (sin su contraseña) y un puerto en el que no escucha nadie.
  agent = spawn(AGENT_EXE, ['--headless', '--insecure-dev', `--pipe=${PIPE_NAME}`, `--trusted-service=${SERVICE_EXE}`,
    `--config=${path.join(DATA_DIR, 'agent.json')}`, '--obs-port=1'], { stdio: 'inherit' });
  console.log(`[run-service] agente pid=${agent.pid}`);
};
setTimeout(startAgent, 1500);

const stop = () => {
  agent?.kill();
  service.kill();
};
process.on('SIGINT', stop);
process.on('SIGTERM', stop);
service.on('exit', (code) => { agent?.kill(); process.exit(code ?? 0); });
