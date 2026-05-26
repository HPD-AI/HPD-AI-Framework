// Main client
export { AgentClient } from './client.js';
export type {
  AgentClientConfig,
  AgentEventHandler,
  EventSubscription,
  MaybePromise,
  TransportType,
} from './client.js';

// Types
export * from './types/index.js';

// Transports (for advanced usage)
export { SseTransport, WebSocketTransport } from './transports/index.js';

// HTTP API
export { AgentHttpApi } from './api.js';

// Parser (for advanced usage)
export { SseParser } from './parser.js';

// Chat runtime
export { ChatManager, ChatSession } from './chat.js';
export type { ChatSessionOptions, OpenChatOptions, SendTextOptions } from './chat.js';

// Client tools
export { ClientToolRegistry, normalizeClientToolName } from './tools.js';
export type { ClientToolHandler, ClientToolHandlerResult } from './tools.js';

// Error handling
export {
  AgentError,
  parseErrorResponse,
  createNetworkError,
  createValidationError,
  createTimeoutError,
  createAbortError,
} from './errors.js';
