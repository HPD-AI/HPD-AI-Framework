export type BaseSuccessStatus = "ok" | "created" | "accepted" | "noContent";

export interface BaseWarning {
  readonly code: string;
  readonly message: string;
}

export interface BaseClientError {
  readonly code: string;
  readonly category:
    | "validation" | "authentication" | "authorization" | "notFound"
    | "conflict" | "unsupported" | "capability" | "store"
    | "unexpected" | "unknownServerError";
  readonly message: string;
}

export type BaseRetryClassification = "never" | "safe" | "identifiedMutationOnly";

export type BaseResult<T> =
  | { readonly ok: true; readonly value: T; readonly status: BaseSuccessStatus; readonly correlationId: string; readonly revision?: RevisionToken; readonly warnings: readonly BaseWarning[] }
  | { readonly ok: false; readonly error: BaseClientError; readonly correlationId?: string; readonly retry: BaseRetryClassification };

declare const revisionBrand: unique symbol;
export type RevisionToken = string & { readonly [revisionBrand]: true };

export class BaseClientException extends Error {
  public constructor(public readonly error: BaseClientError) {
    super(error.message);
    this.name = "BaseClientException";
  }
}

export function unwrap<T>(result: BaseResult<T>): T {
  if (!result.ok) throw new BaseClientException(result.error);
  return result.value;
}
