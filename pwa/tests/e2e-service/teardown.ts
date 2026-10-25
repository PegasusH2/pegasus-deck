// Cierra las instancias de pruebas del agente (la "app" autorizada y el agente E2E),
// identificándolas por su línea de órdenes para no tocar un agente real del usuario.
import { killAgents } from './processes';

export default function teardown(): void {
  killAgents('PegasusDeck.E2E');
}
