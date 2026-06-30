import { createCapabilityIndex } from "./capabilities.js";
import { BaseCollectionClient } from "./collection.js";
import { HpdBaseError } from "./errors.js";
import { createSchemaMetadataIndex } from "./hydration.js";
import { unwrapResult } from "./result.js";
import { encodePathSegment, HttpTransport } from "./transport/http.js";
import type { HttpHeaderOptions } from "./transport/http.js";
import type { BaseManifest, CapabilityDescriptor, CapabilityFeatureDescriptor, CollectionSummaryDescriptor, ExpandedBaseManifest, HydratedBaseMetadata, RouteDescriptor } from "./types/descriptors.js";
import type { JsonObject } from "./types/records.js";
import type { BaseResult } from "./types/results.js";
import type { CollectionDefinition, SchemaMetadata } from "./types/schema.js";
import type { DiagnosticDescriptor, HealthDescriptor } from "./types/health.js";
import type { RecordQueryInput } from "./types/query.js";
import type { CreateInput, DeleteResult, PatchInput, RecordEnvelope, RecordPage, ReplaceInput } from "./types/records.js";

export type MetadataView = "public" | "admin";
export type ManifestExpandToken = "schema" | "capabilities" | "health" | "diagnostics" | "collections";
export type CollectionOperation = "list" | "query" | "get" | "create" | "patch" | "replace" | "delete";

export interface BaseClientConfig {
  /** Mapped BASE HTTP prefix, for example `/base` or `https://host.example/base`. */
  baseUrl: string;
  /** Custom fetch implementation for older runtimes, tests, or instrumented transports. */
  fetch?: typeof globalThis.fetch;
  /** Static or async headers applied to every request. */
  headers?: HeadersInit | (() => HeadersInit | Promise<HeadersInit>);
  /** Fetch credentials mode passed through to every request. */
  credentials?: RequestCredentials;
  /** Optional client name sent as `X-HPD-Client`. */
  clientName?: string;
  /** Optional client version sent as `X-HPD-Client-Version`. */
  clientVersion?: string;
  /** Client-side compatibility metadata; not sent as a BASE-required header. */
  contractVersion?: string;
  /** Default abort signal used when a call does not provide its own signal. */
  defaultSignal?: AbortSignal;
  /** Optional bootstrap defaults for metadata hydration. */
  bootstrap?: BaseBootstrapOptions;
  /** Optional preloaded manifest or expanded manifest. */
  bootstrapManifest?: BaseManifest | ExpandedBaseManifest;
  /** In-memory metadata cache options. */
  cache?: MetadataCacheOptions;
}

export interface BaseBootstrapOptions {
  expand?: ManifestExpandToken[];
  view?: MetadataView;
  diagnostics?: boolean;
}

export interface MetadataCacheOptions {
  mode?: "memory" | "none";
  ttlMs?: number;
  forceRefresh?: boolean;
}

export interface RequestOptions {
  /** Per-call abort signal. */
  signal?: AbortSignal;
  /** Per-call headers merged after configured headers. */
  headers?: HeadersInit;
  /** Correlation id sent as `X-Correlation-ID`. */
  correlationId?: string;
}

export interface ManifestOptions extends RequestOptions {
  expand?: ManifestExpandToken[];
}

export interface BootstrapRequestOptions extends ManifestOptions {
  view?: MetadataView;
  diagnostics?: boolean;
  cache?: MetadataCacheOptions;
}

export interface MetadataRequestOptions extends RequestOptions {
  forceRefresh?: boolean;
  view?: MetadataView;
}

export interface SupportsOptions {
  collectionId?: string;
  view?: MetadataView;
  allowDegraded?: boolean;
}

export interface BaseExtensionHeaderOptions {
  headers?: HeadersInit;
  hasBody?: boolean;
  contentType?: string | false;
  accept?: string | false;
  correlationId?: string;
}

export interface BaseClientExtensionContext {
  readonly baseUrl: string;
  readonly fetch: typeof globalThis.fetch;
  readonly credentials?: RequestCredentials;
  readonly defaultSignal?: AbortSignal;
  url(path: string, query?: URLSearchParams): string;
  headers(options?: BaseExtensionHeaderOptions): Promise<Headers>;
  metadata(options?: MetadataRequestOptions): Promise<HydratedBaseMetadata>;
  metadataResult(options?: MetadataRequestOptions): Promise<BaseResult<HydratedBaseMetadata>>;
  supports(featureId: string, options?: SupportsOptions): boolean | undefined;
  feature(featureId: string, options?: SupportsOptions): CapabilityFeatureDescriptor | undefined;
  requireFeature(featureId: string, options?: SupportsOptions): CapabilityFeatureDescriptor;
}

export interface ListRequestOptions extends RequestOptions {
  method?: "auto" | "get" | "post";
  maxUrlLength?: number;
  validate?: import("./types/query.js").QueryValidationMode;
}

export interface CreateOptions extends RequestOptions {
  requestedId?: string;
  idempotencyKey?: string;
}

export interface MutationOptions extends RequestOptions {
  expectedRevision?: string;
}

export interface DeleteOptions extends MutationOptions {
  returnPrevious?: boolean;
}

export interface HpdBaseClient {
  readonly admin: BaseAdminClient;
  /** Fetches `/manifest`; `expand` requests an expanded manifest. */
  manifest(options?: ManifestOptions): Promise<BaseManifest | ExpandedBaseManifest>;
  manifestResult(options?: ManifestOptions): Promise<BaseResult<BaseManifest | ExpandedBaseManifest>>;
  /** Hydrates manifest/schema/capability/collection descriptor indexes. */
  bootstrap(options?: BootstrapRequestOptions): Promise<HydratedBaseMetadata>;
  bootstrapResult(options?: BootstrapRequestOptions): Promise<BaseResult<HydratedBaseMetadata>>;
  /** Alias for `bootstrap`, with cache refresh controls. */
  metadata(options?: MetadataRequestOptions): Promise<HydratedBaseMetadata>;
  metadataResult(options?: MetadataRequestOptions): Promise<BaseResult<HydratedBaseMetadata>>;
  capabilities(options?: RequestOptions): Promise<CapabilityDescriptor>;
  capabilitiesResult(options?: RequestOptions): Promise<BaseResult<CapabilityDescriptor>>;
  schema(options?: RequestOptions): Promise<SchemaMetadata>;
  schemaResult(options?: RequestOptions): Promise<BaseResult<SchemaMetadata>>;
  collections(options?: RequestOptions): Promise<CollectionDefinition[]>;
  collectionsResult(options?: RequestOptions): Promise<BaseResult<CollectionDefinition[]>>;
  collectionDefinition(id: string, options?: RequestOptions): Promise<CollectionDefinition>;
  collectionDefinitionResult(id: string, options?: RequestOptions): Promise<BaseResult<CollectionDefinition>>;
  health(options?: RequestOptions): Promise<HealthDescriptor[]>;
  healthResult(options?: RequestOptions): Promise<BaseResult<HealthDescriptor[]>>;
  diagnostics(options?: RequestOptions): Promise<DiagnosticDescriptor[]>;
  diagnosticsResult(options?: RequestOptions): Promise<BaseResult<DiagnosticDescriptor[]>>;
  /** Creates a generic collection handle over the implemented record routes. */
  collection<TRecord extends JsonObject = JsonObject>(id: string): CollectionClient<TRecord>;
  /** Descriptor-only feature support lookup; does not probe endpoints. */
  supports(featureId: string, options?: SupportsOptions): boolean | undefined;
  feature(featureId: string, options?: SupportsOptions): CapabilityFeatureDescriptor | undefined;
  requireFeature(featureId: string, options?: SupportsOptions): CapabilityFeatureDescriptor;
  /** Narrow module-client hook for URL, headers, fetch, metadata, and capability reuse. */
  extension(): BaseClientExtensionContext;
}

export interface BaseAdminClient {
  manifest(options?: ManifestOptions): Promise<BaseManifest | ExpandedBaseManifest>;
  manifestResult(options?: ManifestOptions): Promise<BaseResult<BaseManifest | ExpandedBaseManifest>>;
  bootstrap(options?: BootstrapRequestOptions): Promise<HydratedBaseMetadata>;
  bootstrapResult(options?: BootstrapRequestOptions): Promise<BaseResult<HydratedBaseMetadata>>;
  metadata(options?: MetadataRequestOptions): Promise<HydratedBaseMetadata>;
  metadataResult(options?: MetadataRequestOptions): Promise<BaseResult<HydratedBaseMetadata>>;
  capabilities(options?: RequestOptions): Promise<CapabilityDescriptor>;
  capabilitiesResult(options?: RequestOptions): Promise<BaseResult<CapabilityDescriptor>>;
  schema(options?: RequestOptions): Promise<SchemaMetadata>;
  schemaResult(options?: RequestOptions): Promise<BaseResult<SchemaMetadata>>;
  collections(options?: RequestOptions): Promise<CollectionDefinition[]>;
  collectionsResult(options?: RequestOptions): Promise<BaseResult<CollectionDefinition[]>>;
  collectionDefinition(id: string, options?: RequestOptions): Promise<CollectionDefinition>;
  collectionDefinitionResult(id: string, options?: RequestOptions): Promise<BaseResult<CollectionDefinition>>;
  health(options?: RequestOptions): Promise<HealthDescriptor[]>;
  healthResult(options?: RequestOptions): Promise<BaseResult<HealthDescriptor[]>>;
  diagnostics(options?: RequestOptions): Promise<DiagnosticDescriptor[]>;
  diagnosticsResult(options?: RequestOptions): Promise<BaseResult<DiagnosticDescriptor[]>>;
  supports(featureId: string, options?: SupportsOptions): boolean | undefined;
  feature(featureId: string, options?: SupportsOptions): CapabilityFeatureDescriptor | undefined;
  requireFeature(featureId: string, options?: SupportsOptions): CapabilityFeatureDescriptor;
}

export interface CollectionClient<TRecord extends JsonObject = JsonObject> {
  readonly id: string;
  /** Lists records using GET for safe query shapes and POST fallback for complex DTOs. */
  list(query?: RecordQueryInput<TRecord>, options?: ListRequestOptions): Promise<RecordPage<TRecord>>;
  listResult(query?: RecordQueryInput<TRecord>, options?: ListRequestOptions): Promise<BaseResult<RecordPage<TRecord>>>;
  /** Posts a `RecordQuery` DTO to `/collections/{collectionId}/query`. */
  query(query?: RecordQueryInput<TRecord>, options?: RequestOptions): Promise<RecordPage<TRecord>>;
  queryResult(query?: RecordQueryInput<TRecord>, options?: RequestOptions): Promise<BaseResult<RecordPage<TRecord>>>;
  /** Reads one record by id. */
  get(id: string, options?: RequestOptions): Promise<RecordEnvelope<TRecord>>;
  getResult(id: string, options?: RequestOptions): Promise<BaseResult<RecordEnvelope<TRecord>>>;
  /** Creates a record from a plain payload or exact `RecordCreateRequest`. */
  create(input: CreateInput<TRecord>, options?: CreateOptions): Promise<RecordEnvelope<TRecord>>;
  createResult(input: CreateInput<TRecord>, options?: CreateOptions): Promise<BaseResult<RecordEnvelope<TRecord>>>;
  /** Patches top-level fields using BASE field-map patch semantics. */
  patch(id: string, input: PatchInput<TRecord>, options?: MutationOptions): Promise<RecordEnvelope<TRecord>>;
  patchResult(id: string, input: PatchInput<TRecord>, options?: MutationOptions): Promise<BaseResult<RecordEnvelope<TRecord>>>;
  /** Replaces a record payload. */
  replace(id: string, input: ReplaceInput<TRecord>, options?: MutationOptions): Promise<RecordEnvelope<TRecord>>;
  replaceResult(id: string, input: ReplaceInput<TRecord>, options?: MutationOptions): Promise<BaseResult<RecordEnvelope<TRecord>>>;
  /** Deletes a record; sends a body only when revision or previous-record options require it. */
  delete(id: string, options?: DeleteOptions): Promise<DeleteResult<TRecord>>;
  deleteResult(id: string, options?: DeleteOptions): Promise<BaseResult<DeleteResult<TRecord>>>;
  definition(options?: RequestOptions): Promise<CollectionDefinition>;
  definitionResult(options?: RequestOptions): Promise<BaseResult<CollectionDefinition>>;
  supports(operation: CollectionOperation, options?: SupportsOptions): boolean | undefined;
}

interface CacheEntry {
  metadata: HydratedBaseMetadata;
  createdAt: number;
}

type MetadataClientCore = Omit<HpdBaseClient, "admin" | "collection" | "extension"> & { latestMetadata?: HydratedBaseMetadata };

/** Creates a zero-dependency fetch client for the implemented HPD.BASE ASP.NET projection. */
export function createBaseClient(config: BaseClientConfig | string): HpdBaseClient {
  const resolved = typeof config === "string" ? { baseUrl: config } : config;
  if (!(resolved.fetch ?? globalThis.fetch)) {
    throw new HpdBaseError({
      status: "transportError",
      code: "base.client.noFetch",
      message: "HPD.BASE client requires global fetch or config.fetch."
    });
  }
  const transport = new HttpTransport(resolved);
  const cache = new Map<string, CacheEntry>();
  let publicClient!: MetadataClientCore;
  let admin!: MetadataClientCore;
  const resolveView = (requestedView: MetadataView) => requestedView === "admin" ? admin : publicClient;
  publicClient = createMetadataClient("public", transport, cache, resolved, resolveView);
  admin = createMetadataClient("admin", transport, cache, resolved, resolveView);
  const extension = createExtensionContext(transport, publicClient);
  return {
    ...publicClient,
    get latestMetadata() {
      return publicClient.latestMetadata;
    },
    admin,
    collection<TRecord extends JsonObject = JsonObject>(id: string): CollectionClient<TRecord> {
      return new BaseCollectionClient<TRecord>(
        id,
        transport,
        (collectionId, options) => publicClient.collectionDefinition(collectionId, options),
        (collectionId, options) => publicClient.collectionDefinitionResult(collectionId, options),
        (collectionId, operation, options) => collectionSupports(publicClient.latestMetadata, collectionId, operation as CollectionOperation, options)
      );
    },
    extension: () => extension
  };
}

function createExtensionContext(transport: HttpTransport, client: MetadataClientCore): BaseClientExtensionContext {
  return {
    baseUrl: transport.baseUrl,
    fetch: transport.fetch,
    credentials: transport.credentials,
    defaultSignal: transport.defaultSignal,
    url(path: string, query?: URLSearchParams) {
      return transport.url(path, query);
    },
    headers(options?: BaseExtensionHeaderOptions) {
      return transport.headers(options as HttpHeaderOptions | undefined);
    },
    metadata(options?: MetadataRequestOptions) {
      return client.metadata(options);
    },
    metadataResult(options?: MetadataRequestOptions) {
      return client.metadataResult(options);
    },
    supports(featureId: string, options?: SupportsOptions) {
      return client.supports(featureId, options);
    },
    feature(featureId: string, options?: SupportsOptions) {
      return client.feature(featureId, options);
    },
    requireFeature(featureId: string, options?: SupportsOptions) {
      return client.requireFeature(featureId, options);
    }
  };
}

function createMetadataClient(
  view: MetadataView,
  transport: HttpTransport,
  cache: Map<string, CacheEntry>,
  config: BaseClientConfig,
  resolveView: (view: MetadataView) => MetadataClientCore
): MetadataClientCore {
  let latestMetadata: HydratedBaseMetadata | undefined;
  const prefix = view === "admin" ? "/admin" : "";

  const api = {
    get latestMetadata() {
      return latestMetadata;
    },
    async manifest(options?: ManifestOptions) {
      return unwrapResult(await api.manifestResult(options));
    },
    manifestResult(options?: ManifestOptions) {
      const query = expandQuery(options?.expand);
      return transport.request<BaseManifest | ExpandedBaseManifest>({ path: `${prefix}/manifest`, query, headers: options?.headers, signal: options?.signal, correlationId: options?.correlationId });
    },
    async bootstrap(options?: BootstrapRequestOptions) {
      return unwrapResult(await api.bootstrapResult(options));
    },
    async bootstrapResult(options?: BootstrapRequestOptions): Promise<BaseResult<HydratedBaseMetadata>> {
      const mergedOptions = mergeBootstrapOptions(config.bootstrap, options);
      if (mergedOptions.view && mergedOptions.view !== view) {
        return resolveView(mergedOptions.view).bootstrapResult(mergedOptions);
      }
      const cacheMode = mergedOptions.cache?.mode ?? config.cache?.mode ?? "memory";
      const expand = mergedOptions.expand ?? defaultExpand(mergedOptions);
      const cacheKey = `${view}:${expand.join(",")}`;
      const ttl = options?.cache?.ttlMs ?? config.cache?.ttlMs;
      const forceRefresh = mergedOptions.cache?.forceRefresh ?? config.cache?.forceRefresh ?? false;
      const cached = cacheMode === "memory" && !forceRefresh ? cache.get(cacheKey) : undefined;
      if (cached && (ttl === undefined || Date.now() - cached.createdAt <= ttl)) {
        latestMetadata = cached.metadata;
        return { ok: true, status: "ok", value: cached.metadata, httpStatus: 200, headers: {} };
      }
      if (config.bootstrapManifest && cacheMode === "memory" && !forceRefresh && bootstrapManifestMatchesView(config.bootstrapManifest, view)) {
        const hydrated = await hydrateFromManifest(view, config.bootstrapManifest, api, expand.includes("diagnostics"));
        const compatibility = contractCompatibility(hydrated.manifest, config.contractVersion);
        if (compatibility) return compatibility;
        latestMetadata = hydrated;
        cache.set(cacheKey, { metadata: hydrated, createdAt: Date.now() });
        return { ok: true, status: "ok", value: hydrated, httpStatus: 200, headers: {} };
      }
      const manifest = await api.manifestResult({ ...mergedOptions, expand });
      if (!manifest.ok) return manifest;
      const hydrated = await hydrateFromManifest(view, manifest.value, api, expand.includes("diagnostics"));
      const compatibility = contractCompatibility(hydrated.manifest, config.contractVersion);
      if (compatibility) return compatibility;
      latestMetadata = hydrated;
      if (cacheMode === "memory") cache.set(cacheKey, { metadata: hydrated, createdAt: Date.now() });
      return { ok: true, status: "ok", value: hydrated, httpStatus: manifest.httpStatus, headers: manifest.headers };
    },
    async metadata(options?: MetadataRequestOptions) {
      return unwrapResult(await api.metadataResult(options));
    },
    metadataResult(options?: MetadataRequestOptions) {
      return api.bootstrapResult({ ...options, view: options?.view ?? view, cache: { ...config.cache, forceRefresh: options?.forceRefresh } });
    },
    async capabilities(options?: RequestOptions) {
      return unwrapResult(await api.capabilitiesResult(options));
    },
    capabilitiesResult(options?: RequestOptions) {
      return transport.request<CapabilityDescriptor>({ path: `${prefix}/capabilities`, headers: options?.headers, signal: options?.signal, correlationId: options?.correlationId });
    },
    async schema(options?: RequestOptions) {
      return unwrapResult(await api.schemaResult(options));
    },
    schemaResult(options?: RequestOptions) {
      return transport.request<SchemaMetadata>({ path: `${prefix}/schema`, headers: options?.headers, signal: options?.signal, correlationId: options?.correlationId });
    },
    async collections(options?: RequestOptions) {
      return unwrapResult(await api.collectionsResult(options));
    },
    collectionsResult(options?: RequestOptions) {
      return transport.request<CollectionDefinition[]>({ path: `${prefix}/collections`, headers: options?.headers, signal: options?.signal, correlationId: options?.correlationId });
    },
    async collectionDefinition(id: string, options?: RequestOptions) {
      return unwrapResult(await api.collectionDefinitionResult(id, options));
    },
    collectionDefinitionResult(id: string, options?: RequestOptions) {
      return transport.request<CollectionDefinition>({ path: `${prefix}/collections/${encodePathSegment(id)}`, headers: options?.headers, signal: options?.signal, correlationId: options?.correlationId });
    },
    async health(options?: RequestOptions) {
      return unwrapResult(await api.healthResult(options));
    },
    healthResult(options?: RequestOptions) {
      return transport.request<HealthDescriptor[]>({ path: `${prefix}/health`, headers: options?.headers, signal: options?.signal, correlationId: options?.correlationId });
    },
    async diagnostics(options?: RequestOptions) {
      return unwrapResult(await api.diagnosticsResult(options));
    },
    diagnosticsResult(options?: RequestOptions) {
      return transport.request<DiagnosticDescriptor[]>({ path: `${prefix}/diagnostics`, headers: options?.headers, signal: options?.signal, correlationId: options?.correlationId });
    },
    supports(featureId: string, options?: SupportsOptions) {
      return createCapabilityIndex(latestMetadata?.capabilities).supports(featureId, { ...options, view: options?.view ?? view });
    },
    feature(featureId: string, options?: SupportsOptions) {
      return createCapabilityIndex(latestMetadata?.capabilities).feature(featureId, { ...options, view: options?.view ?? view });
    },
    requireFeature(featureId: string, options?: SupportsOptions) {
      return createCapabilityIndex(latestMetadata?.capabilities).require(featureId, { ...options, view: options?.view ?? view });
    }
  };
  return api;
}

async function hydrateFromManifest(
  view: MetadataView,
  manifestOrExpanded: BaseManifest | ExpandedBaseManifest,
  api: Pick<BaseAdminClient, "schemaResult" | "capabilitiesResult" | "collectionsResult" | "healthResult" | "diagnosticsResult">,
  includeDiagnostics: boolean
): Promise<HydratedBaseMetadata> {
  const expanded = isExpandedManifest(manifestOrExpanded) ? manifestOrExpanded : undefined;
  const manifest = expanded?.manifest ?? manifestOrExpanded as BaseManifest;
  const schema = expanded?.schema ?? optionalValue(await api.schemaResult());
  const capabilities = expanded?.capabilities ?? optionalValue(await api.capabilitiesResult());
  const collections = expanded?.collections ?? schema?.collections ?? optionalValue(await api.collectionsResult()) ?? summariesToCollections(manifest.collections);
  const health = expanded?.health ?? optionalValue(await api.healthResult());
  const diagnostics = expanded?.diagnostics ?? (includeDiagnostics ? optionalValue(await api.diagnosticsResult()) : undefined);
  const capabilityIndex = createCapabilityIndex(capabilities);
  const schemaIndex = createSchemaMetadataIndex(schema ? { ...schema, collections } : undefined);
  const routesByOperationId = routeIndex(manifest);
  const etagBySection = new Map<string, string>();
  if (manifest.eTag) etagBySection.set("manifest", manifest.eTag);
  if (expanded?.eTag) etagBySection.set("expandedManifest", expanded.eTag);
  if (schema?.eTag) etagBySection.set("schema", schema.eTag);
  return {
    view,
    manifest,
    schema,
    capabilities,
    health,
    diagnostics,
    collectionsById: new Map(collections.map(collection => [collection.id, collection])),
    featuresById: capabilityIndex.featuresById,
    familiesById: capabilityIndex.familiesById,
    routesByOperationId,
    fieldsByCollectionAndName: schemaIndex.fieldsByCollectionAndName,
    etagBySection
  };
}

function mergeBootstrapOptions(defaults: BaseBootstrapOptions | undefined, options: BootstrapRequestOptions | undefined): BootstrapRequestOptions {
  return {
    ...options,
    expand: options?.expand ?? defaults?.expand,
    view: options?.view ?? defaults?.view,
    diagnostics: options?.diagnostics ?? defaults?.diagnostics,
    cache: options?.cache
  };
}

function defaultExpand(options: BootstrapRequestOptions | undefined): ManifestExpandToken[] {
  const expand: ManifestExpandToken[] = ["schema", "capabilities", "health", "collections"];
  if (options?.diagnostics) expand.push("diagnostics");
  return expand;
}

function expandQuery(expand: ManifestExpandToken[] | undefined): URLSearchParams | undefined {
  if (!expand?.length) return undefined;
  const query = new URLSearchParams();
  query.set("expand", expand.join(","));
  return query;
}

function isExpandedManifest(value: BaseManifest | ExpandedBaseManifest): value is ExpandedBaseManifest {
  return "manifest" in value;
}

function bootstrapManifestMatchesView(value: BaseManifest | ExpandedBaseManifest, view: MetadataView): boolean {
  const manifest = isExpandedManifest(value) ? value.manifest : value;
  return manifest.visibility === view;
}

function optionalValue<T>(result: BaseResult<T>): T | undefined {
  return result.ok ? result.value : undefined;
}

function summariesToCollections(summaries: CollectionSummaryDescriptor[] | undefined): CollectionDefinition[] {
  return (summaries ?? []).map(summary => ({
    id: summary.id,
    name: summary.name,
    displayName: summary.displayName,
    kind: summary.kind,
    enabled: summary.enabled,
    exposed: summary.exposed,
    schemaMode: "loose",
    unknownFields: "preserve",
    requiredCapabilities: summary.requiredFeatureIds
  }));
}

function routeIndex(manifest: BaseManifest): ReadonlyMap<string, RouteDescriptor> {
  const routes = new Map<string, RouteDescriptor>();
  for (const projection of manifest.projections ?? []) {
    for (const route of projection.routes ?? []) routes.set(route.operationId, route);
  }
  return routes;
}

function collectionSupports(metadata: HydratedBaseMetadata | undefined, collectionId: string, operation: CollectionOperation, _options?: SupportsOptions): boolean | undefined {
  const collection = metadata?.collectionsById.get(collectionId);
  if (!collection) return undefined;
  if (collection.enabled === false || collection.exposed === false) return false;
  if (collection.readOnly && (operation === "create" || operation === "patch" || operation === "replace" || operation === "delete")) return false;
  const matrix = collection.operations;
  if (!matrix) return undefined;
  return operation === "query" ? matrix.list : matrix[operation];
}

function contractCompatibility(manifest: BaseManifest, expectedContractVersion: string | undefined): BaseResult<HydratedBaseMetadata> | undefined {
  if (!expectedContractVersion) return undefined;
  const compatibleVersions = manifest.compatibility.compatibleContractVersions;
  const compatible = compatibleVersions?.length
    ? compatibleVersions.includes(expectedContractVersion)
    : manifest.contractVersion === expectedContractVersion;
  return compatible
    ? undefined
    : {
        ok: false,
        status: "validationFailed",
        error: {
          status: "validationFailed",
          code: "base.client.contractVersionMismatch",
          message: `BASE contract '${manifest.contractVersion}' is not compatible with client contract '${expectedContractVersion}'.`,
          category: "validation"
        }
      };
}
