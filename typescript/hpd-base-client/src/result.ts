import { HpdBaseError } from "./errors.js";
import type { BaseResult, BaseResponseHeaders, HpdBaseErrorData, OperationStatus, SuccessOperationStatus } from "./types/results.js";
import type { HpdProblemDetails } from "./types/problem-details.js";

export type OperationContextKind = "read" | "create" | "patch" | "replace" | "delete";

export function unwrapResult<T>(result: BaseResult<T>): T {
  if (result.ok) return result.value;
  throw new HpdBaseError(result.error, {
    httpStatus: result.httpStatus,
    headers: result.headers,
    problem: result.problem
  });
}

export function successStatus(kind: OperationContextKind, httpStatus: number): SuccessOperationStatus {
  if (httpStatus === 201) return "created";
  if (httpStatus === 204) return "noContent";
  if (kind === "patch" || kind === "replace") return "updated";
  if (kind === "delete") return "deleted";
  return "ok";
}

export function fallbackFailureStatus(httpStatus?: number): Exclude<OperationStatus, SuccessOperationStatus> {
  switch (httpStatus) {
    case 400:
      return "validationFailed";
    case 401:
      return "unauthorized";
    case 403:
      return "policyDenied";
    case 404:
      return "notFound";
    case 409:
      return "conflict";
    case 424:
      return "capabilityUnavailable";
    case 500:
    case 503:
    case 504:
      return "storeError";
    default:
      return "transportError";
  }
}

export function makeTransportError(message: string, cause?: unknown, code = "base.client.transport"): HpdBaseErrorData {
  return {
    status: "transportError",
    code,
    message,
    category: "transport",
    ...(cause instanceof DOMException && cause.name === "AbortError" ? { code: "base.client.abort" } : null)
  };
}

export function failureResult<T>(
  error: HpdBaseErrorData,
  options: { httpStatus?: number; headers?: BaseResponseHeaders; problem?: HpdProblemDetails; warnings?: HpdBaseErrorData["warnings"] } = {}
): BaseResult<T> {
  return {
    ok: false,
    status: error.status as Exclude<OperationStatus, SuccessOperationStatus>,
    error,
    httpStatus: options.httpStatus,
    headers: options.headers,
    problem: options.problem,
    warnings: options.warnings
  };
}
