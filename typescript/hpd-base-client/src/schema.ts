export type BaseFieldOperator =
  | "equal" | "notEqual" | "lessThan" | "lessThanOrEqual" | "greaterThan"
  | "greaterThanOrEqual" | "in" | "isNull" | "isDefined" | "between"
  | "contains" | "notContains" | "startsWith" | "endsWith" | "like" | "notLike";

export interface BaseFieldDefinition<T = unknown, TOperators extends readonly BaseFieldOperator[] = readonly BaseFieldOperator[]> {
  readonly id: string;
  readonly wireName: string;
  readonly operators: TOperators;
  readonly __value?: T;
}

export interface BaseVectorIndexDefinition {
  readonly id: string;
  readonly dimensions: number;
  readonly measure: "cosineSimilarity" | "dotProductSimilarity" | "euclideanDistance";
  readonly direction: "higherIsNearer" | "lowerIsNearer";
}

export interface BaseCollectionDefinition<
  TRecord = unknown,
  TCreate = unknown,
  TReplace = unknown,
  TPatch = unknown,
  TFields extends Readonly<Record<string, BaseFieldDefinition>> = Readonly<Record<string, BaseFieldDefinition>>,
  TOperations extends readonly BaseCollectionOperation[] = readonly BaseCollectionOperation[]
> {
  readonly id: string;
  readonly fields: TFields;
  readonly operations: TOperations;
  readonly pagination: "none" | "seek" | "stableHistory";
  readonly maxPageSize: number;
  readonly vectorIndexes: Readonly<Record<string, BaseVectorIndexDefinition>>;
  readonly __record?: TRecord;
  readonly __create?: TCreate;
  readonly __replace?: TReplace;
  readonly __patch?: TPatch;
}

export type BaseCollectionOperation = "list" | "query" | "get" | "create" | "patch" | "replace" | "delete" | "upsert" | "batch" | "watch" | "realtime" | "vector";

export interface BaseGeneratedSchema<
  TCollections extends Readonly<Record<string, BaseCollectionDefinition>> = Readonly<Record<string, BaseCollectionDefinition>>,
  TReads extends Readonly<Record<string, BaseReadDefinition>> = Readonly<Record<string, BaseReadDefinition>>
> {
  readonly protocolMajor: 2;
  readonly schemaGeneration: string;
  readonly digest: string;
  readonly audience: "application" | "controlPlane";
  readonly features: { readonly files: boolean; readonly realtime: boolean; readonly batch: boolean; readonly controlOperations: readonly string[] };
  readonly collections: TCollections;
  readonly reads: TReads;
}

export interface BaseReadDefinition<TParameters = unknown, TRow = unknown, TWatchable extends boolean = boolean> {
  readonly id: string;
  readonly maxPageSize: number;
  readonly watchable: TWatchable;
  readonly __parameters?: TParameters;
  readonly __row?: TRow;
}

export function read<TParameters, TRow, const TWatchable extends boolean>(definition: BaseReadDefinition<TParameters, TRow, TWatchable>): BaseReadDefinition<TParameters, TRow, TWatchable> { return deepFreeze(definition); }

export function collection<TRecord, TCreate, TReplace, TPatch, TFields extends Readonly<Record<string, BaseFieldDefinition>>, const TOperations extends readonly BaseCollectionOperation[]>(
  definition: BaseCollectionDefinition<TRecord, TCreate, TReplace, TPatch, TFields, TOperations>
): BaseCollectionDefinition<TRecord, TCreate, TReplace, TPatch, TFields, TOperations> {
  return deepFreeze(definition);
}

export function field<T, const TOperators extends readonly BaseFieldOperator[]>(id: string, wireName: string, operators: TOperators): BaseFieldDefinition<T, TOperators> {
  return Object.freeze({ id, wireName, operators: Object.freeze([...operators]) as unknown as TOperators });
}

function deepFreeze<T>(value: T): T {
  if (typeof value !== "object" || value === null || Object.isFrozen(value)) return value;
  for (const item of Object.values(value)) deepFreeze(item);
  return Object.freeze(value);
}
