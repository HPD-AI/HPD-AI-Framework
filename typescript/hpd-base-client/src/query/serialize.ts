import type { FilterExpression, QueryExtension, QueryValue, RecordQuery } from "../types/query.js";

export interface SerializedQuery {
  ok: boolean;
  search?: URLSearchParams;
  reason?: string;
}

const operatorToGet = new Map<string, string>([
  ["equal", ""],
  ["notEqual", "neq"],
  ["lessThan", "lt"],
  ["lessThanOrEqual", "lte"],
  ["greaterThan", "gt"],
  ["greaterThanOrEqual", "gte"],
  ["contains", "contains"],
  ["notContains", "notContains"],
  ["startsWith", "startsWith"],
  ["endsWith", "endsWith"],
  ["like", "like"],
  ["notLike", "notLike"]
]);

/** Serializes only the implemented ASP.NET GET query grammar. */
export function serializeRecordQueryForGet(query: RecordQuery, maxUrlLength = 1800): SerializedQuery {
  const search = new URLSearchParams();
  const filters = flattenGetFilters(query.filter);
  if (!filters.ok) return { ok: false, reason: filters.reason };
  for (const filter of filters.filters) {
    const result = appendFilter(search, filter);
    if (!result.ok) return result;
  }

  if (query.sort?.length) {
    search.set("sort", query.sort.map(sort => `${sort.direction === "desc" ? "-" : ""}${sort.field}`).join(","));
    for (const sort of query.sort) {
      if (sort.nulls && sort.nulls !== "unspecified") search.set(`nulls[${sort.field}]`, sort.nulls);
    }
  }

  if (query.page) {
    if (query.page.mode === "cursor") {
      if (query.page.cursor) search.set("cursor", query.page.cursor);
      if (query.page.cursorDirection) search.set("cursorDir", query.page.cursorDirection);
      if (query.page.limit !== undefined) search.set("limit", String(query.page.limit));
    } else if (query.page.mode === "offset") {
      if (query.page.offset !== undefined) search.set("offset", String(query.page.offset));
      if (query.page.limit !== undefined) search.set("limit", String(query.page.limit));
    } else {
      if (query.page.page !== undefined) search.set("page", String(query.page.page));
      if (query.page.perPage !== undefined) search.set("perPage", String(query.page.perPage));
    }
  }

  if (query.select?.length) search.set("select", query.select.join(","));
  if (query.include?.length) {
    if (query.include.some(include => include.filter || include.sort?.length || include.limit !== undefined || include.select?.length)) {
      return { ok: false, reason: "GET include supports path-only includes." };
    }
    search.set("include", query.include.map(include => include.path).join(","));
  }
  if (query.count) search.set("count", query.count);
  if (query.requestDependencyToken) search.set("dependencyToken", "true");
  const extensionResult = appendExtensions(search, query.extensions);
  if (!extensionResult.ok) return extensionResult;

  return search.toString().length > maxUrlLength
    ? { ok: false, reason: "Serialized query exceeds maxUrlLength." }
    : { ok: true, search };
}

function flattenGetFilters(filter: FilterExpression | undefined): { ok: true; filters: FilterExpression[] } | { ok: false; reason: string } {
  if (!filter) return { ok: true, filters: [] };
  if (filter.kind === "and") {
    const children = filter.children ?? [];
    if (children.some(child => child.kind === "and" || child.kind === "or" || child.kind === "not")) {
      return { ok: false, reason: "Nested boolean filters require POST." };
    }
    return { ok: true, filters: children };
  }
  if (filter.kind === "compare" || filter.kind === "in" || filter.kind === "isNull" || filter.kind === "isDefined") return { ok: true, filters: [filter] };
  return { ok: false, reason: `${filter.kind} filters require POST.` };
}

function appendFilter(search: URLSearchParams, filter: FilterExpression): SerializedQuery {
  if (!filter.field) return { ok: false, reason: "GET-safe filter requires a field." };
  if (filter.kind === "isNull" || filter.kind === "isDefined") {
    search.set(`where[${filter.field}][${filter.kind}]`, "true");
    return { ok: true, search };
  }
  if (filter.kind === "in") {
    const values = filter.values ?? [];
    if (!areGetValues(values)) return { ok: false, reason: "Typed or complex values require POST." };
    search.set(`where[${filter.field}][in]`, values.map(stringifySimpleValue).join(","));
    return { ok: true, search };
  }
  const value = filter.value;
  if (!value) return { ok: false, reason: "Compare filter requires a value." };
  if (!areGetValues([value])) return { ok: false, reason: "Typed or complex values require POST." };
  const op = operatorToGet.get(filter.operator ?? "equal");
  if (op === undefined) return { ok: false, reason: `Unsupported GET operator '${filter.operator}'.` };
  search.set(op ? `where[${filter.field}][${op}]` : `where[${filter.field}]`, stringifySimpleValue(value));
  return { ok: true, search };
}

function appendExtensions(search: URLSearchParams, extensions: QueryExtension[] | undefined): SerializedQuery {
  for (const extension of extensions ?? []) {
    const args = extension.arguments ?? [];
    if (args.length === 0 || args.some(arg => !isSimpleValue(arg))) return { ok: false, reason: "Complex query extensions require POST." };
    search.set(`ext[${extension.moduleId}.${extension.name}]`, args.map(stringifySimpleValue).join(","));
  }
  return { ok: true, search };
}

function areGetValues(values: QueryValue[]): boolean {
  return values.every(value => isSimpleValue(value));
}

function isSimpleValue(value: QueryValue): boolean {
  return value.kind === "string" || value.kind === "boolean" || value.kind === "integer";
}

function stringifySimpleValue(value: QueryValue): string {
  if (value.kind === "string") return value.string ?? "";
  if (value.kind === "boolean") return String(value.boolean);
  if (value.kind === "integer") return String(value.integer);
  throw new Error("Only string, boolean, and integer values are GET-safe.");
}
