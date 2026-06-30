import type { QueryValidationOptions, QueryValidationResult, RecordQuery } from "../types/query.js";

export function validateRecordQuery(query: RecordQuery, options: QueryValidationOptions = {}): QueryValidationResult {
  const issues = [];
  if (query.page?.limit !== undefined && query.page.limit <= 0) {
    issues.push({ code: "base.client.query.limit", message: "Query limit must be positive.", path: "page.limit", severity: "error" as const });
  }
  if (query.page?.page !== undefined && query.page.page < 1) {
    issues.push({ code: "base.client.query.page", message: "Query page must be 1 or greater.", path: "page.page", severity: "error" as const });
  }
  if (query.page?.offset !== undefined && query.page.offset < 0) {
    issues.push({ code: "base.client.query.offset", message: "Query offset cannot be negative.", path: "page.offset", severity: "error" as const });
  }
  const serializedLength = JSON.stringify(query).length;
  if (options.maxSerializedLength !== undefined && serializedLength > options.maxSerializedLength) {
    issues.push({ code: "base.client.query.tooLarge", message: "Query exceeds maxSerializedLength.", path: "query", severity: "error" as const });
  }
  return { ok: issues.length === 0, issues };
}
