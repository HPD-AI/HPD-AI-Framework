/**
 * Error handling utilities for HPD-Agent SDK.
 * Provides structured error types for better error handling and debugging.
 */

/**
 * Structured error class for HPD-Agent API errors.
 * Provides detailed error information including HTTP status codes and validation details.
 *
 * @example
 * ```typescript
 * try {
 *   await client.branches.delete('session-123', 'branch-456');
 * } catch (error) {
 *   if (error instanceof AgentError && error.code === 'CONFLICT') {
 *     console.error('Cannot delete:', error.details);
 *     // { "HasChildren": ["Cannot delete branch with 3 child branches..."] }
 *   }
 * }
 * ```
 */
export class AgentError extends Error {
  /**
   * Error code for categorizing errors.
   * Common codes: CONFLICT, BAD_REQUEST, NOT_FOUND, UNAUTHORIZED, FORBIDDEN, INTERNAL_SERVER_ERROR
   */
  public readonly code: string;

  /**
   * HTTP status code if this error originated from an HTTP response.
   */
  public readonly statusCode?: number;

  /**
   * Validation error details from the backend.
   * Format: { fieldName: ["error message 1", "error message 2"] }
   *
   * Example V3 validation errors:
   * - { "HasChildren": ["Cannot delete branch with 3 children..."] }
   * - { "ProtectedBranch": ["Cannot delete the 'main' branch."] }
   * - { "IsStreaming": ["Branch is actively streaming and cannot be cancelled"] }
   */
  public readonly details?: Record<string, string[]>;

  /**
   * Original error that caused this error (if any).
   */
  public readonly cause?: Error;

  constructor(
    message: string,
    code: string,
    options?: {
      statusCode?: number;
      details?: Record<string, string[]>;
      cause?: Error;
    }
  ) {
    super(message);
    this.name = 'AgentError';
    this.code = code;
    this.statusCode = options?.statusCode;
    this.details = options?.details;
    this.cause = options?.cause;

    // Maintains proper stack trace for where our error was thrown (only available on V8)
    if ('captureStackTrace' in Error) {
      (Error as any).captureStackTrace(this, AgentError);
    }
  }

  /**
   * Get a user-friendly error message that includes validation details.
   */
  getUserMessage(): string {
    if (!this.details) {
      return this.message;
    }

    // Format validation errors
    const detailMessages = Object.entries(this.details)
      .flatMap(([field, messages]) => messages.map((msg) => `${field}: ${msg}`))
      .join('; ');

    return `${this.message}. ${detailMessages}`;
  }

  /**
   * Check if this error has a specific error code.
   */
  is(code: string): boolean {
    return this.code === code;
  }

  /**
   * Check if this error is a conflict error (409).
   */
  isConflict(): boolean {
    return this.code === 'CONFLICT';
  }

  /**
   * Check if this error is a validation error (400).
   */
  isBadRequest(): boolean {
    return this.code === 'BAD_REQUEST';
  }

  /**
   * Check if this error is a not found error (404).
   */
  isNotFound(): boolean {
    return this.code === 'NOT_FOUND';
  }

  /**
   * Check if this error is an authorization error (401).
   */
  isUnauthorized(): boolean {
    return this.code === 'UNAUTHORIZED';
  }

  /**
   * Check if this error is a forbidden error (403).
   */
  isForbidden(): boolean {
    return this.code === 'FORBIDDEN';
  }

  /**
   * Serialize error to JSON for logging/debugging.
   */
  toJSON(): Record<string, unknown> {
    return {
      name: this.name,
      message: this.message,
      code: this.code,
      statusCode: this.statusCode,
      details: this.details,
      stack: this.stack,
    };
  }
}

/**
 * Parse error response from HPD-Agent backend.
 * Converts HTTP responses into structured AgentError instances.
 *
 * @param response - The fetch Response object
 * @param body - Parsed response body (if available)
 * @returns AgentError instance with structured error information
 *
 * @example
 * ```typescript
 * const response = await fetch('/api/sessions/123/branches/456', { method: 'DELETE' });
 * if (!response.ok) {
 *   const body = await response.json().catch(() => null);
 *   throw parseErrorResponse(response, body);
 * }
 * ```
 */
export function parseErrorResponse(response: Response, body?: any): AgentError {
  // Extract error details from response body
  const title = body?.title || body?.error || body?.message;
  const details = body?.errors as Record<string, string[]> | undefined;

  // Map HTTP status to error code
  let code = 'UNKNOWN';
  let message = title || `HTTP ${response.status}`;

  switch (response.status) {
    case 400:
      code = 'BAD_REQUEST';
      message = title || 'Bad Request';
      break;

    case 401:
      code = 'UNAUTHORIZED';
      message = title || 'Unauthorized';
      break;

    case 403:
      code = 'FORBIDDEN';
      message = title || 'Forbidden';
      break;

    case 404:
      code = 'NOT_FOUND';
      message = title || 'Resource not found';
      break;

    case 409:
      code = 'CONFLICT';
      message = title || 'Conflict';
      break;

    case 422:
      code = 'VALIDATION_ERROR';
      message = title || 'Validation failed';
      break;

    case 429:
      code = 'RATE_LIMITED';
      message = title || 'Too many requests';
      break;

    case 500:
      code = 'INTERNAL_SERVER_ERROR';
      message = title || 'Internal server error';
      break;

    case 502:
      code = 'BAD_GATEWAY';
      message = title || 'Bad gateway';
      break;

    case 503:
      code = 'SERVICE_UNAVAILABLE';
      message = title || 'Service unavailable';
      break;

    case 504:
      code = 'GATEWAY_TIMEOUT';
      message = title || 'Gateway timeout';
      break;
  }

  return new AgentError(message, code, {
    statusCode: response.status,
    details,
  });
}

/**
 * Create an AgentError for network/connection errors.
 */
export function createNetworkError(message: string, cause?: Error): AgentError {
  return new AgentError(message, 'NETWORK_ERROR', { cause });
}

/**
 * Create an AgentError for client-side validation errors.
 */
export function createValidationError(
  message: string,
  details?: Record<string, string[]>
): AgentError {
  return new AgentError(message, 'VALIDATION_ERROR', { details });
}

/**
 * Create an AgentError for timeout errors.
 */
export function createTimeoutError(message: string): AgentError {
  return new AgentError(message, 'TIMEOUT');
}

/**
 * Create an AgentError for abort errors.
 */
export function createAbortError(message: string = 'Request aborted'): AgentError {
  return new AgentError(message, 'ABORTED');
}
