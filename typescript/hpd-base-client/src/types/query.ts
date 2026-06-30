export type FilterNodeKind =
  | "true"
  | "false"
  | "not"
  | "and"
  | "or"
  | "compare"
  | "in"
  | "between"
  | "isNull"
  | "isDefined"
  | "extension";

export type FilterOperator =
  | "equal"
  | "notEqual"
  | "lessThan"
  | "lessThanOrEqual"
  | "greaterThan"
  | "greaterThanOrEqual"
  | "contains"
  | "notContains"
  | "startsWith"
  | "endsWith"
  | "like"
  | "notLike";

export type QueryValueKind = "null" | "string" | "boolean" | "integer" | "number" | "decimal" | "dateTime" | "id" | "array";
export type QuerySortDirection = "asc" | "desc";
export type QueryNullOrder = "unspecified" | "first" | "last";
export type QueryPaginationMode = "page" | "offset" | "cursor";
export type QueryCursorDirection = "after" | "before";
export type QueryCountMode = "none" | "ifAvailable" | "exact" | "estimated" | "limited";

export interface RecordQuery {
  filter?: FilterExpression;
  sort?: QuerySort[];
  page?: QueryPage;
  select?: string[];
  include?: QueryInclude[];
  count?: QueryCountMode;
  requestDependencyToken?: boolean;
  extensions?: QueryExtension[];
}

export interface FilterExpression {
  kind: FilterNodeKind;
  field?: string;
  operator?: FilterOperator;
  value?: QueryValue;
  values?: QueryValue[];
  children?: FilterExpression[];
  moduleId?: string;
  name?: string;
  arguments?: QueryValue[];
}

export interface QueryValue {
  kind: QueryValueKind;
  string?: string;
  boolean?: boolean;
  integer?: number;
  number?: number;
  decimal?: string;
  dateTime?: string;
  id?: string;
  array?: QueryValue[];
}

export interface QuerySort {
  field: string;
  direction?: QuerySortDirection;
  nulls?: QueryNullOrder;
}

export interface QueryPage {
  mode?: QueryPaginationMode;
  page?: number;
  perPage?: number;
  offset?: number;
  limit?: number;
  cursor?: string;
  cursorDirection?: QueryCursorDirection;
}

export interface QueryInclude {
  path: string;
  select?: string[];
  filter?: FilterExpression;
  sort?: QuerySort[];
  limit?: number;
}

export interface QueryExtension {
  moduleId: string;
  name: string;
  arguments?: QueryValue[];
}

export type FieldPath<TRecord = unknown> = TRecord extends object ? Extract<keyof TRecord, string> | string : string;

export type QueryValueInput = QueryValue | null | string | boolean | number | bigint | Date | QueryValueInput[];

export interface QueryIncludeInput<TRecord = unknown> {
  select?: FieldPath<TRecord>[];
  filter?: FilterExpression | ((q: import("../query/builder.js").QueryHelpers) => FilterExpression);
  sort?: QuerySort | QuerySort[];
  limit?: number;
}

export interface RecordQueryInput<TRecord = unknown> {
  where?: FilterExpression | ((q: import("../query/builder.js").QueryHelpers) => FilterExpression);
  filter?: FilterExpression | ((q: import("../query/builder.js").QueryHelpers) => FilterExpression);
  sort?: QuerySort | QuerySort[];
  page?: QueryPage;
  select?: FieldPath<TRecord>[];
  include?: QueryInclude | QueryInclude[];
  count?: QueryCountMode;
  requestDependencyToken?: boolean;
  dependencyToken?: boolean;
  extensions?: QueryExtension[];
}

export type QueryValidationMode = "off" | "warn" | "strict";

export interface QueryValidationOptions {
  capabilities?: import("./descriptors.js").CapabilityDescriptor;
  collectionId?: string;
  mode?: QueryValidationMode;
  maxSerializedLength?: number;
}

export interface QueryValidationIssue {
  code: string;
  message: string;
  path?: string;
  severity?: "warning" | "error";
}

export interface QueryValidationResult {
  ok: boolean;
  issues: QueryValidationIssue[];
}
