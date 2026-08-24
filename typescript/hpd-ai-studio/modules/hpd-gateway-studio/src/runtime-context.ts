import type { GatewayDeclarationController } from './declaration-state.ts';
import type { GatewayManagedWorkflowController } from './managed-workflows.ts';
import type { GatewayOperationsController } from './operations.ts';
import type { GatewayQuickRouteCoordinator } from './quick-route.ts';
import type { GatewayStudioController } from './state.ts';
import type { GatewayObservabilityLink } from './observability-links.ts';

/** Module-private typed authority shared only by the Gateway activation and its static pages. */
export interface GatewayRuntimeContext {
  readonly controller: GatewayStudioController;
  readonly declaration: GatewayDeclarationController;
  readonly quickRoute: GatewayQuickRouteCoordinator;
  readonly workflows: GatewayManagedWorkflowController;
  readonly operations: GatewayOperationsController;
  readonly observabilityLinks: readonly GatewayObservabilityLink[];
}

let active: GatewayRuntimeContext | null = null;

/** @internal Installs one principal-generation-owned Gateway context. */
export function installGatewayRuntimeContext(value: GatewayRuntimeContext): () => void {
  if (active !== null) throw new Error('Gateway Studio already has an active principal generation.');
  active = value;
  return () => { if (active === value) active = null; };
}

/** Returns the current typed Gateway context or fails closed after disposal. */
export function requireGatewayRuntimeContext(): GatewayRuntimeContext {
  if (active === null) throw new Error('Gateway Studio activation authority is unavailable.');
  return active;
}
