import type { CapabilityDescriptor, CollectionDefinition, FieldDefinition } from "@hpd/base-client";
import type { RecordOperation } from "./types.js";

export const recordOperations: RecordOperation[] = ["list", "query", "get", "create", "patch", "replace", "delete"];
const featureByOperation: Record<RecordOperation, string> = {
  list: "records.list",
  query: "records.query",
  get: "records.get",
  create: "records.create",
  patch: "records.patch",
  replace: "records.replace",
  delete: "records.delete"
};
const methodByOperation: Record<RecordOperation, string> = {
  list: "get",
  query: "post",
  get: "get",
  create: "post",
  patch: "patch",
  replace: "put",
  delete: "delete"
};

export function planOperations(collection: CollectionDefinition, capabilities?: CapabilityDescriptor, openApi?: unknown): RecordOperation[] {
  if (collection.enabled === false || collection.exposed === false) return [];
  if (!requiredCapabilitiesAvailable(collection.requiredCapabilities, collection.id, capabilities)) return [];
  return recordOperations.filter(operation => operationAvailable(collection, operation, capabilities, openApi));
}

export function fieldRequiredCapabilitiesAvailable(field: FieldDefinition, collectionId: string, capabilities?: CapabilityDescriptor): boolean {
  return requiredCapabilitiesAvailable(field.requiredCapabilities, collectionId, capabilities);
}

export function planQueryFeatures(collectionId: string, capabilities?: CapabilityDescriptor) {
  return {
    filter: anyFeatureAvailable(["records.filter", "records.query.filter", "query.filter"], collectionId, capabilities),
    sort: anyFeatureAvailable(["records.sort", "records.query.sort", "query.sort"], collectionId, capabilities),
    select: anyFeatureAvailable(["records.select", "records.query.select", "query.select"], collectionId, capabilities),
    include: anyFeatureAvailable(["records.include", "records.query.include", "query.include"], collectionId, capabilities)
  };
}

export function routeForOperation(openApi: unknown, operation: RecordOperation, collectionId: string): { method: string; path: string; operationId?: string; requiredFeatureIds?: string[]; visibility?: string } | undefined {
  if (!isObject(openApi) || !isObject(openApi.paths)) return undefined;
  const method = methodByOperation[operation];
  for (const path of routeTemplates(operation, collectionId)) {
    const operationObject = isObject(openApi.paths?.[path]) ? openApi.paths[path]?.[method] : undefined;
    if (isObject(operationObject)) {
      return pruneUndefined({
        method: method.toUpperCase(),
        path,
        operationId: typeof operationObject.operationId === "string" ? operationObject.operationId : undefined,
        requiredFeatureIds: stringArray(operationObject["x-hpd-required-feature-ids"]),
        visibility: typeof operationObject["x-hpd-route-visibility"] === "string" ? operationObject["x-hpd-route-visibility"] : undefined
      });
    }
  }
  return undefined;
}

function operationAvailable(collection: CollectionDefinition, operation: RecordOperation, capabilities?: CapabilityDescriptor, openApi?: unknown): boolean {
  if (collection.readOnly === true && ["create", "patch", "replace", "delete"].includes(operation)) return false;
  if ((collection.operations as Record<string, boolean | undefined> | undefined)?.[operation] === false) return false;
  if (!featureAvailable(featureByOperation[operation], collection.id, capabilities)) return false;
  if (openApi && !openApiHasRoute(openApi, operation, collection.id)) return false;
  return true;
}

function featureAvailable(featureId: string, collectionId: string, capabilities?: CapabilityDescriptor): boolean {
  return anyFeatureAvailable([featureId], collectionId, capabilities);
}

function anyFeatureAvailable(featureIds: string[], collectionId: string, capabilities?: CapabilityDescriptor): boolean {
  if (!capabilities) return true;
  const features = capabilities.families.flatMap(family => family.features ?? []);
  const matches = features.filter(feature => {
    if (!featureIds.includes(feature.featureId)) return false;
    return !feature.appliesTo || feature.appliesTo.length === 0 || feature.appliesTo.includes(collectionId) || feature.appliesTo.includes("*");
  });
  if (matches.length === 0) return true;
  return matches.some(feature => (feature.status ?? "available") === "available");
}

function requiredCapabilitiesAvailable(featureIds: string[] | undefined, collectionId: string, capabilities?: CapabilityDescriptor): boolean {
  if (!featureIds || featureIds.length === 0) return true;
  return featureIds.every(featureId => featureAvailable(featureId, collectionId, capabilities));
}

function openApiHasRoute(openApi: unknown, operation: RecordOperation, collectionId: string): boolean {
  if (!isObject(openApi) || !isObject(openApi.paths)) return true;
  return routeForOperation(openApi, operation, collectionId) !== undefined;
}

function routeTemplates(operation: RecordOperation, collectionId: string): string[] {
  if (operation === "list" || operation === "create") {
    return [`/collections/${collectionId}/records`, `/base/collections/${collectionId}/records`, "/collections/{collectionId}/records", "/base/collections/{collectionId}/records"];
  }
  if (operation === "query") {
    return [`/collections/${collectionId}/query`, `/base/collections/${collectionId}/query`, "/collections/{collectionId}/query", "/base/collections/{collectionId}/query"];
  }
  return [`/collections/${collectionId}/records/{id}`, `/base/collections/${collectionId}/records/{id}`, "/collections/{collectionId}/records/{id}", "/base/collections/{collectionId}/records/{id}"];
}

function isObject(input: unknown): input is Record<string, any> {
  return typeof input === "object" && input !== null && !Array.isArray(input);
}

function stringArray(input: unknown): string[] | undefined {
  return Array.isArray(input) && input.every(value => typeof value === "string") ? input : undefined;
}

function pruneUndefined<T extends Record<string, unknown>>(input: T): T {
  return Object.fromEntries(Object.entries(input).filter(([, value]) => value !== undefined)) as T;
}
