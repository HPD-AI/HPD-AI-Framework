import { readFile } from "node:fs/promises";
import type { BaseManifest, CapabilityDescriptor } from "@hpd/base-client";
import type { CollectionDefinition, SchemaMetadata } from "@hpd/base-client";
import { parseGeneratorConfig } from "./config.js";
import type { BaseClientGenerationSnapshot, GenerateOptions, GeneratorConfig } from "./types.js";

export async function readJsonFile(path: string): Promise<unknown> {
  const raw = await readFile(path, "utf8");
  try {
    return JSON.parse(raw);
  } catch (error) {
    throw new Error(`Invalid JSON in ${path}: ${error instanceof Error ? error.message : String(error)}`);
  }
}

export async function loadGeneratorConfig(path?: string, banner?: string): Promise<GeneratorConfig> {
  const parsed = parseGeneratorConfig(path ? await readJsonFile(path) : {});
  return banner ? { ...parsed, banner } : parsed;
}

export async function loadSnapshot(options: GenerateOptions): Promise<BaseClientGenerationSnapshot> {
  if (options.snapshot) {
    return parseSnapshot(await readJsonFile(options.snapshot));
  }
  if (!options.manifest || !options.schema) {
    throw new Error("Expected --snapshot or at least --manifest and --schema.");
  }
  const manifest = await readJsonFile(options.manifest) as BaseManifest;
  const schema = await readJsonFile(options.schema) as SchemaMetadata;
  const capabilities = options.capabilities ? await readJsonFile(options.capabilities) as CapabilityDescriptor : undefined;
  const collections = options.collections ? await readJsonFile(options.collections) as CollectionDefinition[] : schema.collections;
  const openApi = options.openapi ? await readJsonFile(options.openapi) : undefined;
  return parseSnapshot({ snapshotVersion: "1", manifest, schema, capabilities, collections, openApi });
}

export function parseSnapshot(input: unknown): BaseClientGenerationSnapshot {
  if (!isObject(input)) throw new Error("Snapshot must be a JSON object.");
  if (input.snapshotVersion !== "1") throw new Error(`Unsupported snapshotVersion ${String(input.snapshotVersion)}.`);
  if (!isObject(input.manifest)) throw new Error("Snapshot is missing manifest.");
  if (!isObject(input.schema)) throw new Error("Snapshot is missing schema.");
  const schema = input.schema as unknown as SchemaMetadata;
  const collections = Array.isArray(input.collections) ? input.collections as CollectionDefinition[] : schema.collections;
  if (!Array.isArray(collections)) throw new Error("Snapshot schema.collections must be an array.");
  return {
    snapshotVersion: "1",
    generatedAt: typeof input.generatedAt === "string" ? input.generatedAt : undefined,
    source: isObject(input.source) ? input.source as BaseClientGenerationSnapshot["source"] : undefined,
    manifest: input.manifest as unknown as BaseManifest,
    schema: { ...schema, collections },
    capabilities: isObject(input.capabilities) ? input.capabilities as unknown as CapabilityDescriptor : undefined,
    collections,
    openApi: input.openApi
  };
}

function isObject(input: unknown): input is Record<string, unknown> {
  return typeof input === "object" && input !== null && !Array.isArray(input);
}
