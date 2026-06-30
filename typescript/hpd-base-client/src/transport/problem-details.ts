import { fallbackFailureStatus } from "../result.js";
import type { BaseResponseHeaders, HpdBaseErrorData } from "../types/results.js";
import type { HpdProblemDetails } from "../types/problem-details.js";

export async function parseFailureResponse(response: Response, headers: BaseResponseHeaders): Promise<{ error: HpdBaseErrorData; problem?: HpdProblemDetails }> {
  const contentType = response.headers.get("content-type") ?? "";
  if (!contentType.includes("json")) {
    const text = await response.text().catch(() => "");
    return {
      error: {
        status: fallbackFailureStatus(response.status),
        code: `hpd.http.${response.status}`,
        message: text || `${response.status} ${response.statusText}`,
        correlationId: headers.correlationId
      }
    };
  }

  const body = await response.json().catch(() => null);
  if (!body || typeof body !== "object") {
    return {
      error: {
        status: "transportError",
        code: "base.client.invalidProblemDetails",
        message: "BASE returned a malformed JSON failure body.",
        correlationId: headers.correlationId
      }
    };
  }

  const problem = body as HpdProblemDetails;
  const status = stringExtension(problem, "hpd.status") ?? fallbackFailureStatus(response.status);
  const code = stringExtension(problem, "hpd.error.code") ?? `hpd.http.${response.status}`;
  const message = typeof problem.detail === "string" && problem.detail.length > 0
    ? problem.detail
    : typeof problem.title === "string" && problem.title.length > 0
      ? problem.title
      : `${response.status} ${response.statusText}`;

  return {
    problem,
    error: {
      status: status as HpdBaseErrorData["status"],
      code,
      message,
      category: stringExtension(problem, "hpd.error.category"),
      target: stringExtension(problem, "hpd.error.target"),
      correlationId: stringExtension(problem, "hpd.error.correlationId") ?? headers.correlationId,
      validation: arrayExtension(problem, "hpd.validation"),
      conflict: objectExtension(problem, "hpd.conflict"),
      capability: objectExtension(problem, "hpd.capability"),
      policy: objectExtension(problem, "hpd.policy"),
      store: objectExtension(problem, "hpd.store"),
      warnings: arrayExtension(problem, "hpd.warnings"),
      diagnostics: stringRecordExtension(problem, "hpd.diagnostics"),
      problem
    }
  };
}

function stringExtension(problem: HpdProblemDetails, key: string): string | undefined {
  const value = problem[key];
  return typeof value === "string" ? value : undefined;
}

function objectExtension<T extends object>(problem: HpdProblemDetails, key: string): T | undefined {
  const value = problem[key];
  return value && typeof value === "object" && !Array.isArray(value) ? value as T : undefined;
}

function arrayExtension<T>(problem: HpdProblemDetails, key: string): T[] | undefined {
  const value = problem[key];
  return Array.isArray(value) ? value as T[] : undefined;
}

function stringRecordExtension(problem: HpdProblemDetails, key: string): Record<string, string> | undefined {
  const value = objectExtension<Record<string, unknown>>(problem, key);
  if (!value) return undefined;
  return Object.fromEntries(Object.entries(value).filter((entry): entry is [string, string] => typeof entry[1] === "string"));
}
