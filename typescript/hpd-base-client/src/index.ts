export { createBaseClient } from "./client.js";
export type {
  BaseAdminClient,
  BaseBootstrapOptions,
  BaseClientConfig,
  BaseClientExtensionContext,
  BaseExtensionHeaderOptions,
  BootstrapRequestOptions,
  CollectionClient,
  CollectionOperation,
  CreateOptions,
  DeleteOptions,
  HpdBaseClient,
  ListRequestOptions,
  ManifestExpandToken,
  ManifestOptions,
  MetadataCacheOptions,
  MetadataRequestOptions,
  MetadataView,
  MutationOptions,
  RequestOptions,
  SupportsOptions
} from "./client.js";
export { HpdBaseError, isHpdBaseError } from "./errors.js";
export { unwrapResult } from "./result.js";
export { q, createQueryBuilder, toRecordQuery } from "./query/index.js";
export { createCapabilityIndex } from "./capabilities.js";
export { createSchemaMetadataIndex, hydrateRecord, parseBaseDate, recordCreatedAtDate, recordUpdatedAtDate } from "./hydration.js";
export type * from "./types/index.js";
