import type { CollectionDefinition } from "@hpd/base-client";
import { fieldRequiredCapabilitiesAvailable, planOperations, planQueryFeatures, recordOperations, routeForOperation } from "./capabilities.js";
import { safePropertyName, safeTypeName, uniqueName } from "./names.js";
import { planField } from "./type-map.js";
import type { BaseClientGenerationSnapshot, GenerationPlan, GeneratorConfig, PlannedCollection } from "./types.js";

export function createGenerationPlan(snapshot: BaseClientGenerationSnapshot, config: GeneratorConfig): GenerationPlan {
  const warnings: string[] = [];
  if (!snapshot.openApi) warnings.push("OpenAPI was not supplied; route liveness was not cross-checked.");
  const propertyNames = new Set<string>();
  const typeNames = new Set<string>();
  const collections = [...(snapshot.collections ?? snapshot.schema.collections ?? [])]
    .sort((left, right) => left.id.localeCompare(right.id))
    .filter(collection => collection.exposed !== false)
    .map(collection => planCollection(collection, snapshot, config, propertyNames, typeNames));
  return { snapshot, config, collections, warnings };
}

function planCollection(
  collection: CollectionDefinition,
  snapshot: BaseClientGenerationSnapshot,
  config: GeneratorConfig,
  propertyNames: Set<string>,
  typeNames: Set<string>
): PlannedCollection {
  const override = config.collectionNameOverrides[collection.id];
  const propertyName = uniqueName(override ?? sdkPropertyName(collection) ?? safePropertyName(collection.id), propertyNames);
  const typeName = uniqueName(safeTypeName(propertyName), typeNames);
  const fieldNames = new Set<string>();
  return {
    source: collection,
    id: collection.id,
    propertyName,
    typeName,
    variableName: propertyName,
    fields: [...(collection.fields ?? [])]
      .filter(field => fieldRequiredCapabilitiesAvailable(field, collection.id, snapshot.capabilities))
      .sort((left, right) => left.name.localeCompare(right.name))
      .map(field => ({ ...planField(field, config), propertyName: uniqueName(planField(field, config).propertyName, fieldNames) })),
    availableOperations: planOperations(collection, snapshot.capabilities, snapshot.openApi),
    operations: config.unsupportedMethods === "runtime-errors" ? [...recordOperations] : planOperations(collection, snapshot.capabilities, snapshot.openApi),
    routes: Object.fromEntries(recordOperations.flatMap(operation => {
      const route = routeForOperation(snapshot.openApi, operation, collection.id);
      return route ? [[operation, route]] : [];
    })),
    queryFeatures: planQueryFeatures(collection.id, snapshot.capabilities)
  };
}

function sdkPropertyName(collection: CollectionDefinition): string | undefined {
  const sdk = collection.extensions?.sdk;
  if (sdk && typeof sdk === "object" && "propertyName" in sdk && typeof sdk.propertyName === "string") return sdk.propertyName;
  return undefined;
}
