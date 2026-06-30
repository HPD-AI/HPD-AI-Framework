import type { CapabilityFeatureDescriptor, HpdBaseClient, RouteDescriptor } from "@hpd/base-client";
import { fileFeatureIds } from "./capabilities.js";
import { BaseFileBucketClientImpl, type BaseFileBucketClient } from "./bucket.js";
import { fileRouteOperationIds, fileRoutePath, normalizeRoutePrefix } from "./routes.js";
import type { BaseFilesClientOptions, FileOperation, FileRouteOptions, FileSupportsOptions } from "./types/options.js";

export interface BaseFilesClient {
  readonly base: HpdBaseClient;
  readonly routePrefix: string;
  bucket(bucketId: string): BaseFileBucketClient;
  supports(operation: FileOperation, options?: FileSupportsOptions): boolean | undefined;
  route(operation: FileOperation, options?: FileRouteOptions): string | undefined;
}

export function createBaseFilesClient(base: HpdBaseClient, options: BaseFilesClientOptions = {}): BaseFilesClient {
  const routePrefix = normalizeRoutePrefix(options.routePrefix);
  const capabilityMode = options.capabilities ?? "check-allow-degraded";
  const extension = base.extension();

  const api: BaseFilesClient = {
    base,
    routePrefix,
    bucket(bucketId: string) {
      return new BaseFileBucketClientImpl(bucketId, extension, routePrefix, api.supports);
    },
    supports(operation: FileOperation, supportOptions?: FileSupportsOptions) {
      if (capabilityMode === "off") return true;
      const metadata = latestMetadata(base);
      const route = routeDescriptor(base, operation);
      const routePresent = route !== undefined;
      const feature = featureDescriptor(base, operation, supportOptions);
      if (!metadata) return undefined;
      if (!routePresent) return false;
      if ((supportOptions?.requireRoute || capabilityMode === "route-presence") && !routePresent) return false;
      if (capabilityMode === "route-presence") return routePresent;
      if (!feature) return true;
      if (feature.status === undefined || feature.status === "available") return true;
      const allowDegraded = supportOptions?.allowDegraded ?? capabilityMode === "check-allow-degraded";
      if (feature.status === "degraded" && allowDegraded) return true;
      return false;
    },
    route(operation: FileOperation, routeOptions?: FileRouteOptions) {
      if (!routeDescriptor(base, operation)) return undefined;
      return fileRoutePath(routePrefix, operation, routeOptions);
    }
  };
  return api;
}

function routeDescriptor(base: HpdBaseClient, operation: FileOperation): RouteDescriptor | undefined {
  return latestMetadata(base)?.routesByOperationId.get(fileRouteOperationIds[operation]);
}

function featureDescriptor(base: HpdBaseClient, operation: FileOperation, options?: FileSupportsOptions): CapabilityFeatureDescriptor | undefined {
  const featureId = operation === "head" ? fileFeatureIds.metadata : fileFeatureIds[operation];
  return base.feature(featureId, { allowDegraded: options?.allowDegraded });
}

function latestMetadata(base: HpdBaseClient) {
  return (base as unknown as { latestMetadata?: unknown }).latestMetadata as { routesByOperationId: ReadonlyMap<string, RouteDescriptor> } | undefined;
}

export type { BaseFileBucketClient } from "./bucket.js";
