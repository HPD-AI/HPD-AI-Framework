export type BaseFieldOperator =
  | "equal" | "notEqual" | "lessThan" | "lessThanOrEqual" | "greaterThan"
  | "greaterThanOrEqual" | "in" | "isNull" | "isDefined" | "between"
  | "contains" | "notContains" | "startsWith" | "endsWith" | "like" | "notLike";

export interface BaseFieldDefinition<T = unknown, TOperators extends readonly BaseFieldOperator[] = readonly BaseFieldOperator[]> {
  readonly id: string;
  readonly wireName: string;
  readonly valueTypeId?: string;
  readonly disclosureShape: "none" | "omission" | "fixed-marker";
  readonly operators: TOperators;
  readonly __value?: T;
}

export interface BaseVectorIndexDefinition {
  readonly id: string;
  readonly dimensions: number;
  readonly measure: "cosineSimilarity" | "dotProductSimilarity" | "euclideanDistance";
  readonly direction: "higherIsNearer" | "lowerIsNearer";
}

export interface BaseTextIndexDefinition {
  readonly id: string;
  readonly version: number;
  readonly maximumResults: number;
  readonly maximumQueryNodes: number;
  readonly maximumQueryDepth: number;
  readonly maximumPhraseTerms: number;
  readonly maximumQueryBytes: number;
  readonly maximumFilterNodes: number;
  readonly maximumFilterDepth: number;
  readonly maximumFilterLiterals: number;
  readonly maximumInValues: number;
  readonly maximumSecondaryOrderFields: number;
  readonly maximumCursorBytes: number;
  readonly fields: Readonly<Record<string, { readonly id: string; readonly wireName: string }>>;
  readonly filterFields: Readonly<Record<string, { readonly id: string; readonly wireName: string; readonly valueKind: "String" | "Boolean" | "Integer" | "Id" }>>;
}

export interface BaseCollectionDefinition<
  TRecord = unknown,
  TCreate = unknown,
  TReplace = unknown,
  TPatch = unknown,
  TFields extends Readonly<Record<string, BaseFieldDefinition>> = Readonly<Record<string, BaseFieldDefinition>>,
  TOperations extends readonly BaseCollectionOperation[] = readonly BaseCollectionOperation[],
  TTextIndexes extends Readonly<Record<string, BaseTextIndexDefinition>> = Readonly<Record<string, BaseTextIndexDefinition>>
> {
  readonly id: string;
  readonly fields: TFields;
  readonly operations: TOperations;
  readonly pagination: "none" | "seek" | "stableHistory";
  readonly maxPageSize: number;
  readonly vectorIndexes: Readonly<Record<string, BaseVectorIndexDefinition>>;
  readonly textIndexes: TTextIndexes;
  readonly recordTypeId?: string;
  readonly createTypeId?: string;
  readonly replaceTypeId?: string;
  readonly patchTypeId?: string;
  readonly __record?: TRecord;
  readonly __create?: TCreate;
  readonly __replace?: TReplace;
  readonly __patch?: TPatch;
}

export type BaseCollectionOperation = "list" | "query" | "get" | "create" | "patch" | "replace" | "delete" | "upsert" | "batch" | "watch" | "realtime" | "vector" | "text";

export interface BaseGeneratedSchema<
  TCollections extends Readonly<Record<string, BaseCollectionDefinition>> = Readonly<Record<string, BaseCollectionDefinition>>,
  TReads extends Readonly<Record<string, BaseReadDefinition>> = Readonly<Record<string, BaseReadDefinition>>,
  TSelections extends Readonly<Record<string, import("./selection.js").BaseSelectionMutationDefinition>> = Readonly<Record<string, import("./selection.js").BaseSelectionMutationDefinition>>,
  TModules extends Readonly<Record<string, import("./module-mutations.js").BaseModuleMutationDefinition>> = Readonly<Record<string, import("./module-mutations.js").BaseModuleMutationDefinition>>,
  TSemantic extends Readonly<Record<string, BaseSemanticActivationDefinition>> = Readonly<Record<string, BaseSemanticActivationDefinition>>
> {
  readonly protocolMajor: 2;
  readonly schemaGeneration: string;
  readonly digest: string;
  readonly audience: "application" | "controlPlane";
  readonly features: { readonly files: boolean; readonly realtime: boolean; readonly batch: boolean; readonly controlOperations: readonly string[] };
  readonly typeGraph?: import("./codec.js").BaseTypeGraph;
  readonly collections: TCollections;
  readonly reads: TReads;
  readonly selectionMutations?: TSelections;
  readonly moduleMutations?: TModules;
  readonly semanticActivations?: TSemantic;
}

export interface BaseSemanticActivationDefinition { readonly id: string; readonly version: number; readonly checksum: string; readonly compactable: boolean; readonly removable: boolean; }
export function semanticActivationDefinition<const T extends BaseSemanticActivationDefinition>(definition: T): T { return deepFreeze(definition); }

export interface BaseReadDefinition<TParameters = unknown, TRow = unknown, TWatchable extends boolean = boolean> {
  readonly id: string;
  readonly parameterTypeId?: string;
  readonly rowTypeId?: string;
  readonly maxPageSize: number;
  readonly watchable: TWatchable;
  readonly __parameters?: TParameters;
  readonly __row?: TRow;
}

export function read<TParameters, TRow, const TWatchable extends boolean>(definition: BaseReadDefinition<TParameters, TRow, TWatchable>): BaseReadDefinition<TParameters, TRow, TWatchable> { return deepFreeze(definition); }

export function collection<TRecord, TCreate, TReplace, TPatch, TFields extends Readonly<Record<string, BaseFieldDefinition>>, const TOperations extends readonly BaseCollectionOperation[], const TTextIndexes extends Readonly<Record<string, BaseTextIndexDefinition>> = Readonly<Record<string, BaseTextIndexDefinition>>>(
  definition: BaseCollectionDefinition<TRecord, TCreate, TReplace, TPatch, TFields, TOperations, TTextIndexes>
): BaseCollectionDefinition<TRecord, TCreate, TReplace, TPatch, TFields, TOperations, TTextIndexes> {
  return deepFreeze(definition);
}

export function field<T, const TOperators extends readonly BaseFieldOperator[]>(id: string, wireName: string, operators: TOperators, valueTypeId?: string, disclosureShape: "none" | "omission" | "fixed-marker" = "none"): BaseFieldDefinition<T, TOperators> {
  return Object.freeze({ id, wireName, operators: Object.freeze([...operators]) as unknown as TOperators, ...(valueTypeId === undefined ? {} : { valueTypeId }), disclosureShape });
}

function deepFreeze<T>(value: T): T {
  if (typeof value !== "object" || value === null || Object.isFrozen(value)) return value;
  for (const item of Object.values(value)) deepFreeze(item);
  return Object.freeze(value);
}
