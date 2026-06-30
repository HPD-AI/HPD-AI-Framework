import type { BaseResponseHeaders, HpdBaseErrorData, OperationStatus } from "./types/results.js";
import type { HpdProblemDetails } from "./types/problem-details.js";

/** Error thrown by convenience methods when BASE returns a failure or transport cannot complete. */
export class HpdBaseError extends Error {
  readonly status: OperationStatus;
  readonly code: string;
  readonly httpStatus?: number;
  readonly data: HpdBaseErrorData;
  readonly headers?: BaseResponseHeaders;
  readonly problem?: HpdProblemDetails;
  override readonly cause?: unknown;

  constructor(data: HpdBaseErrorData, options: { httpStatus?: number; headers?: BaseResponseHeaders; problem?: HpdProblemDetails; cause?: unknown } = {}) {
    super(data.message);
    this.name = "HpdBaseError";
    this.status = data.status;
    this.code = data.code;
    this.httpStatus = options.httpStatus;
    this.data = data;
    this.headers = options.headers;
    this.problem = options.problem;
    this.cause = options.cause;
  }
}

export function isHpdBaseError(value: unknown): value is HpdBaseError {
  return value instanceof HpdBaseError;
}
