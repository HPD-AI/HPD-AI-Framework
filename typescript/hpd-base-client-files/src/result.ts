import { failureResult, makeTransportError, successStatus } from "@hpd/base-client/result";
import { extractResponseHeaders } from "@hpd/base-client/transport";
import { parseFailureResponse } from "@hpd/base-client/transport";
import type { BaseClientExtensionContext } from "@hpd/base-client";
import type { BaseResult } from "@hpd/base-client/types";
import type { FileOperation } from "./types/options.js";

export interface RawFileRequestOptions {
  extension: BaseClientExtensionContext;
  operation: FileOperation;
  method: string;
  path: string;
  query?: URLSearchParams;
  body?: BodyInit;
  headers?: HeadersInit;
  signal?: AbortSignal;
  correlationId?: string;
  contentType?: string | false;
  accept?: string | false;
}

export async function rawRequest(options: RawFileRequestOptions): Promise<BaseResult<Response>> {
  try {
    const response = await options.extension.fetch(options.extension.url(options.path, options.query), {
      method: options.method,
      credentials: options.extension.credentials,
      signal: options.signal ?? options.extension.defaultSignal,
      headers: await options.extension.headers({
        headers: options.headers,
        hasBody: options.body !== undefined,
        contentType: options.contentType,
        accept: options.accept,
        correlationId: options.correlationId
      }),
      ...(options.body === undefined ? null : { body: options.body })
    });
    const headers = extractResponseHeaders(response.headers);
    if (!response.ok) {
      const parsed = await parseFailureResponse(response, headers);
      return failureResult(parsed.error, {
        httpStatus: response.status,
        headers,
        problem: parsed.problem,
        warnings: parsed.error.warnings
      });
    }
    return {
      ok: true,
      status: successStatus(options.operation === "upload" ? "create" : options.operation === "delete" ? "delete" : "read", response.status),
      value: response,
      httpStatus: response.status,
      headers
    };
  } catch (cause) {
    return failureResult(makeTransportError(cause instanceof Error ? cause.message : "BASE files request failed.", cause, "base.files.client.transport"));
  }
}

export async function parseJsonResult<T>(result: BaseResult<Response>): Promise<BaseResult<T>> {
  if (!result.ok) return result;
  return { ...result, value: await result.value.json() as T };
}

export function voidResult(result: BaseResult<Response>): BaseResult<void> {
  if (!result.ok) return result;
  return { ...result, value: undefined };
}
