import type {
  FieldPath,
  FilterExpression,
  FilterOperator,
  QueryCursorDirection,
  QueryExtension,
  QueryInclude,
  QueryIncludeInput,
  QueryNullOrder,
  QueryPage,
  QuerySort,
  QuerySortDirection,
  QueryValue,
  QueryValueInput,
  RecordQuery,
  RecordQueryInput
} from "../types/query.js";

export interface QueryHelpers {
  query<TRecord = unknown>(input?: RecordQueryInput<TRecord>): RecordQuery;
  value(input: QueryValueInput): QueryValue;
  null(): QueryValue;
  string(value: string): QueryValue;
  boolean(value: boolean): QueryValue;
  integer(value: number | bigint): QueryValue;
  number(value: number): QueryValue;
  decimal(value: string | number | bigint): QueryValue;
  dateTime(value: string | Date): QueryValue;
  id(value: string): QueryValue;
  array(values: QueryValueInput[]): QueryValue;
  true(): FilterExpression;
  false(): FilterExpression;
  eq<TRecord>(field: FieldPath<TRecord>, value: QueryValueInput): FilterExpression;
  neq<TRecord>(field: FieldPath<TRecord>, value: QueryValueInput): FilterExpression;
  lt<TRecord>(field: FieldPath<TRecord>, value: QueryValueInput): FilterExpression;
  lte<TRecord>(field: FieldPath<TRecord>, value: QueryValueInput): FilterExpression;
  gt<TRecord>(field: FieldPath<TRecord>, value: QueryValueInput): FilterExpression;
  gte<TRecord>(field: FieldPath<TRecord>, value: QueryValueInput): FilterExpression;
  contains<TRecord>(field: FieldPath<TRecord>, value: QueryValueInput): FilterExpression;
  notContains<TRecord>(field: FieldPath<TRecord>, value: QueryValueInput): FilterExpression;
  startsWith<TRecord>(field: FieldPath<TRecord>, value: string): FilterExpression;
  endsWith<TRecord>(field: FieldPath<TRecord>, value: string): FilterExpression;
  like<TRecord>(field: FieldPath<TRecord>, value: string): FilterExpression;
  notLike<TRecord>(field: FieldPath<TRecord>, value: string): FilterExpression;
  in<TRecord>(field: FieldPath<TRecord>, values: QueryValueInput[]): FilterExpression;
  between<TRecord>(field: FieldPath<TRecord>, min: QueryValueInput, max: QueryValueInput): FilterExpression;
  isNull<TRecord>(field: FieldPath<TRecord>): FilterExpression;
  isDefined<TRecord>(field: FieldPath<TRecord>): FilterExpression;
  and(...children: FilterExpression[]): FilterExpression;
  or(...children: FilterExpression[]): FilterExpression;
  not(child: FilterExpression): FilterExpression;
  filterExtension<TRecord>(moduleId: string, name: string, options?: { field?: FieldPath<TRecord>; arguments?: QueryValueInput[] }): FilterExpression;
  sort<TRecord>(field: FieldPath<TRecord>, direction?: QuerySortDirection, nulls?: QueryNullOrder): QuerySort;
  sortAsc<TRecord>(field: FieldPath<TRecord>, nulls?: QueryNullOrder): QuerySort;
  sortDesc<TRecord>(field: FieldPath<TRecord>, nulls?: QueryNullOrder): QuerySort;
  page(page: number, perPage?: number): QueryPage;
  offset(offset: number, limit?: number): QueryPage;
  cursor(cursor?: string, limit?: number, direction?: QueryCursorDirection): QueryPage;
  include<TRecord>(path: FieldPath<TRecord>, options?: QueryIncludeInput<TRecord>): QueryInclude;
  extension(moduleId: string, name: string, args?: QueryValueInput[]): QueryExtension;
}

export const q: QueryHelpers = createQueryBuilder();

export function createQueryBuilder(): QueryHelpers {
  const helpers: QueryHelpers = {
    query: toRecordQuery,
    value,
    null: () => ({ kind: "null" }),
    string: string => ({ kind: "string", string }),
    boolean: boolean => ({ kind: "boolean", boolean }),
    integer,
    number,
    decimal: decimal => ({ kind: "decimal", decimal: String(decimal) }),
    dateTime: dateTime => ({ kind: "dateTime", dateTime: dateTime instanceof Date ? dateTime.toISOString() : dateTime }),
    id: id => ({ kind: "id", id }),
    array: values => ({ kind: "array", array: values.map(value) }),
    true: () => ({ kind: "true" }),
    false: () => ({ kind: "false" }),
    eq: (field, queryValue) => compare(field, "equal", queryValue),
    neq: (field, queryValue) => compare(field, "notEqual", queryValue),
    lt: (field, queryValue) => compare(field, "lessThan", queryValue),
    lte: (field, queryValue) => compare(field, "lessThanOrEqual", queryValue),
    gt: (field, queryValue) => compare(field, "greaterThan", queryValue),
    gte: (field, queryValue) => compare(field, "greaterThanOrEqual", queryValue),
    contains: (field, queryValue) => compare(field, "contains", queryValue),
    notContains: (field, queryValue) => compare(field, "notContains", queryValue),
    startsWith: (field, queryValue) => compare(field, "startsWith", queryValue),
    endsWith: (field, queryValue) => compare(field, "endsWith", queryValue),
    like: (field, queryValue) => compare(field, "like", queryValue),
    notLike: (field, queryValue) => compare(field, "notLike", queryValue),
    in: (field, values) => {
      if (values.length === 0) throw new Error("Query in() requires at least one value.");
      return { kind: "in", field: String(field), values: values.map(value) };
    },
    between: (field, min, max) => ({ kind: "between", field: String(field), values: [value(min), value(max)] }),
    isNull: field => ({ kind: "isNull", field: String(field) }),
    isDefined: field => ({ kind: "isDefined", field: String(field) }),
    and: (...children) => bool("and", children),
    or: (...children) => bool("or", children),
    not: child => ({ kind: "not", children: [child] }),
    filterExtension: (moduleId, name, options) => extensionFilter(moduleId, name, options),
    sort: (field, direction = "asc", nulls = "unspecified") => ({ field: String(field), direction, nulls }),
    sortAsc: (field, nulls = "unspecified") => ({ field: String(field), direction: "asc", nulls }),
    sortDesc: (field, nulls = "unspecified") => ({ field: String(field), direction: "desc", nulls }),
    page: (page, perPage) => ({ mode: "page", page, perPage }),
    offset: (offset, limit) => ({ mode: "offset", offset, limit }),
    cursor: (cursor, limit, direction = "after") => ({ mode: "cursor", cursor, limit, cursorDirection: direction }),
    include: (path, options) => include(path, options),
    extension: (moduleId, name, args) => queryExtension(moduleId, name, args)
  };
  return helpers;
}

export function toRecordQuery<TRecord = unknown>(input?: RecordQueryInput<TRecord>): RecordQuery {
  if (!input) return {};
  const filter = resolveFilter(input.where ?? input.filter);
  const sort = input.sort ? array(input.sort) : undefined;
  const include = input.include ? array(input.include) : undefined;
  return pruneUndefined({
    filter,
    sort,
    page: input.page,
    select: input.select?.map(String),
    include,
    count: input.count,
    requestDependencyToken: input.requestDependencyToken ?? input.dependencyToken,
    extensions: input.extensions
  });
}

function value(input: QueryValueInput): QueryValue {
  if (isQueryValue(input)) return input;
  if (input === null) return { kind: "null" };
  if (typeof input === "string") return { kind: "string", string: input };
  if (typeof input === "boolean") return { kind: "boolean", boolean: input };
  if (typeof input === "bigint") return integer(input);
  if (typeof input === "number") return Number.isInteger(input) ? integer(input) : number(input);
  if (input instanceof Date) {
    if (Number.isNaN(input.getTime())) throw new Error("Query dateTime value must be a valid Date.");
    return { kind: "dateTime", dateTime: input.toISOString() };
  }
  if (Array.isArray(input)) return { kind: "array", array: input.map(value) };
  throw new Error("Unsupported query value input.");
}

function integer(input: number | bigint): QueryValue {
  const asNumber = typeof input === "bigint" ? Number(input) : input;
  if (!Number.isSafeInteger(asNumber)) throw new Error("Query integer must be a safe integer.");
  return { kind: "integer", integer: asNumber };
}

function number(input: number): QueryValue {
  if (!Number.isFinite(input)) throw new Error("Query number must be finite.");
  return { kind: "number", number: input };
}

function compare<TRecord>(field: FieldPath<TRecord>, operator: FilterOperator, queryValue: QueryValueInput): FilterExpression {
  return { kind: "compare", field: String(field), operator, value: value(queryValue) };
}

function bool(kind: "and" | "or", children: FilterExpression[]): FilterExpression {
  if (children.length === 0) throw new Error(`Query ${kind}() requires at least one child.`);
  return { kind, children };
}

function extensionFilter<TRecord>(moduleId: string, name: string, options?: { field?: FieldPath<TRecord>; arguments?: QueryValueInput[] }): FilterExpression {
  if (!moduleId || !name) throw new Error("Query extension filters require moduleId and name.");
  return pruneUndefined({
    kind: "extension" as const,
    moduleId,
    name,
    field: options?.field ? String(options.field) : undefined,
    arguments: options?.arguments?.map(value)
  });
}

function include<TRecord>(path: FieldPath<TRecord>, options?: QueryIncludeInput<TRecord>): QueryInclude {
  const filter = typeof options?.filter === "function" ? options.filter(q) : options?.filter;
  return pruneUndefined({
    path: String(path),
    select: options?.select?.map(String),
    filter,
    sort: options?.sort ? array(options.sort) : undefined,
    limit: options?.limit
  });
}

function queryExtension(moduleId: string, name: string, args?: QueryValueInput[]): QueryExtension {
  if (!moduleId || !name) throw new Error("Query extensions require moduleId and name.");
  return pruneUndefined({ moduleId, name, arguments: args?.map(value) });
}

function resolveFilter(filter: RecordQueryInput["filter"]): FilterExpression | undefined {
  return typeof filter === "function" ? filter(q) : filter;
}

function array<T>(input: T | T[]): T[] {
  return Array.isArray(input) ? input : [input];
}

function isQueryValue(input: QueryValueInput): input is QueryValue {
  return Boolean(input && typeof input === "object" && "kind" in input && typeof (input as QueryValue).kind === "string");
}

function pruneUndefined<T extends Record<string, unknown>>(valueToPrune: T): T {
  return Object.fromEntries(Object.entries(valueToPrune).filter(([, valueEntry]) => valueEntry !== undefined)) as T;
}
