import type { HpdProblemDetails } from "./problem-details.js";

export type OperationStatus =
  | "ok"
  | "created"
  | "updated"
  | "deleted"
  | "noContent"
  | "notFound"
  | "conflict"
  | "validationFailed"
  | "policyDenied"
  | "unauthorized"
  | "unsupported"
  | "capabilityUnavailable"
  | "storeError"
  | "transportError";

export type SuccessOperationStatus = "ok" | "created" | "updated" | "deleted" | "noContent";

export type BaseResult<T> =
  | {
      ok: true;
      status: SuccessOperationStatus;
      value: T;
      httpStatus: number;
      headers: BaseResponseHeaders;
      warnings?: OperationWarning[];
      diagnostics?: OperationDiagnostics;
    }
  | {
      ok: false;
      status: Exclude<OperationStatus, SuccessOperationStatus>;
      error: HpdBaseErrorData;
      httpStatus?: number;
      headers?: BaseResponseHeaders;
      problem?: HpdProblemDetails;
      warnings?: OperationWarning[];
      diagnostics?: OperationDiagnostics;
    };

export interface BaseResponseHeaders {
  etag?: string;
  revision?: string;
  lastModified?: string;
  location?: string;
  correlationId?: string;
  eventIds?: string[];
  retryAfter?: string;
  preferenceApplied?: string[];
}

export interface HpdBaseErrorData {
  status: OperationStatus;
  code: string;
  message: string;
  category?: string;
  target?: string;
  correlationId?: string;
  validation?: ValidationIssue[];
  conflict?: ConflictInfo;
  capability?: CapabilityErrorInfo;
  policy?: PolicyErrorInfo;
  store?: StoreErrorInfo;
  warnings?: OperationWarning[];
  diagnostics?: Record<string, string>;
  problem?: HpdProblemDetails;
}

export interface ValidationIssue {
  path?: string;
  code?: string;
  message?: string;
  severity?: string;
}

export interface ConflictInfo {
  kind?: string;
  currentRevision?: string;
  expectedRevision?: string;
  conflictingId?: string;
  details?: Record<string, unknown>;
}

export interface CapabilityErrorInfo {
  featureId?: string;
  reason?: string;
  requiredStatus?: string;
  actualStatus?: string;
  details?: Record<string, unknown>;
}

export interface PolicyErrorInfo {
  outcome?: string;
  policyRef?: string;
  reasonCode?: string;
  details?: Record<string, unknown>;
}

export interface StoreErrorInfo {
  storeId?: string;
  providerCode?: string;
  retryable?: boolean;
  details?: Record<string, unknown>;
}

export interface OperationWarning {
  code: string;
  message?: string;
  target?: string;
  details?: Record<string, unknown>;
}

export interface OperationDiagnostics {
  correlationId?: string;
  safeData?: Record<string, string>;
}
