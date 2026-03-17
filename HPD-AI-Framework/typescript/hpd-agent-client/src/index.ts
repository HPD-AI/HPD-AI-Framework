// Main client
export { AgentClient } from './client.js';
export type {
  AgentClientConfig,
  EventHandlers,
  PermissionResponse,
  StreamOptions,
  TransportType,
} from './client.js';

// Types
export * from './types/index.js';

// Transports (for advanced usage)
export { SseTransport, WebSocketTransport, MauiTransport } from './transports/index.js';

// Parser (for advanced usage)
export { SseParser } from './parser.js';

// Error handling
export {
  AgentError,
  parseErrorResponse,
  createNetworkError,
  createValidationError,
  createTimeoutError,
  createAbortError,
} from './errors.js';
