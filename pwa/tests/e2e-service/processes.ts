import { execFileSync } from 'node:child_process';

function stopAgentsWhere(condition: string): void {
  const script = `Get-CimInstance Win32_Process -Filter "Name='PegasusDeck.Agent.exe' OR Name='PegasusDeck.TestApp.exe'" | ` +
    `Where-Object { ${condition} } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }`;
  execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], { stdio: 'ignore' });
}

/** Cierra los procesos PegasusDeck.Agent de pruebas (su línea de órdenes contiene `marker`); nunca un agente real. */
export function killAgents(marker: string): void {
  stopAgentsWhere(`$_.CommandLine -like '*${marker}*'`);
}

/** Cierra solo el agente de sesión E2E (el que confía en el servicio): simula que el usuario cierra sesión. */
export function killSessionAgent(): void {
  stopAgentsWhere(`$_.CommandLine -like '*PegasusDeck.E2E*' -and $_.CommandLine -like '*--trusted-service*'`);
}
