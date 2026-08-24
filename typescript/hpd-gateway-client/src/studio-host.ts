/**
 * Host-only Gateway Studio client construction surface.
 *
 * Application modules must consume the sealed client lease supplied by HPD Studio;
 * they never receive this constructor, an origin, a bearer token, or raw fetch.
 */
export { createGatewayStudioClient } from './client.js';
export type { GatewayStudioClientHostContext, GatewayStudioTransport, GatewayStudioTransportRequest } from './client.js';
