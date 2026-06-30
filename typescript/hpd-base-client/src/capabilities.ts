import { HpdBaseError } from "./errors.js";
import type { CapabilityDescriptor, CapabilityFamilyDescriptor, CapabilityFeatureDescriptor } from "./types/descriptors.js";
import type { SupportsOptions } from "./client.js";

export interface CapabilityIndex {
  supports(featureId: string, options?: SupportsOptions): boolean | undefined;
  feature(featureId: string, options?: SupportsOptions): CapabilityFeatureDescriptor | undefined;
  require(featureId: string, options?: SupportsOptions): CapabilityFeatureDescriptor;
  readonly featuresById: ReadonlyMap<string, CapabilityFeatureDescriptor>;
  readonly familiesById: ReadonlyMap<string, CapabilityFamilyDescriptor>;
}

export function createCapabilityIndex(capabilities: CapabilityDescriptor | undefined): CapabilityIndex {
  const featuresById = new Map<string, CapabilityFeatureDescriptor>();
  const familiesById = new Map<string, CapabilityFamilyDescriptor>();
  for (const family of capabilities?.families ?? []) {
    familiesById.set(family.familyId, family);
    for (const feature of family.features ?? []) featuresById.set(feature.featureId, feature);
  }

  return {
    featuresById,
    familiesById,
    supports(featureId, options) {
      const feature = selectFeature(featuresById.get(featureId), options);
      if (!feature) return capabilities ? false : undefined;
      if (feature.status === "available") return true;
      if (options?.allowDegraded && feature.status === "degraded") return true;
      return false;
    },
    feature(featureId, options) {
      return selectFeature(featuresById.get(featureId), options);
    },
    require(featureId, options) {
      const feature = selectFeature(featuresById.get(featureId), options);
      if (feature && (feature.status === "available" || (options?.allowDegraded && feature.status === "degraded"))) return feature;
      throw new HpdBaseError({
        status: "capabilityUnavailable",
        code: "base.client.capabilityUnavailable",
        message: `BASE capability '${featureId}' is not available.`,
        capability: { featureId, actualStatus: feature?.status }
      });
    }
  };
}

function selectFeature(feature: CapabilityFeatureDescriptor | undefined, options: SupportsOptions | undefined): CapabilityFeatureDescriptor | undefined {
  if (!feature) return undefined;
  if (options?.collectionId && feature.appliesTo?.length && !feature.appliesTo.includes(options.collectionId)) return undefined;
  if (feature.visibility && options?.view && feature.visibility !== options.view) return undefined;
  return feature;
}
