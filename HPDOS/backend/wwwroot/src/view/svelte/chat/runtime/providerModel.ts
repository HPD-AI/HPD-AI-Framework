import type { RunConfig } from "@hpd/hpd-agent-client";

export type ProviderModelRef = {
  providerKey: string;
  modelId: string;
};

export type ProviderModelSelection = ProviderModelRef;

export type ProviderModelVisibility = "visible" | "hidden";
export type ProviderModelStatus = "active" | "beta" | "alpha" | "deprecated";
export type ModelCatalogTag = "tools" | "reasoning" | "vision" | "free" | "local";

export type ModelCatalogFilter = {
  query?: string;
  tags?: ModelCatalogTag[];
};

export type ModelCatalogTagOption = {
  tag: ModelCatalogTag;
  label: string;
};

export type ProviderModelUiState = {
  selected?: ProviderModelSelection;
  recent: ProviderModelRef[];
  favorites: ProviderModelRef[];
  visibility: Record<string, ProviderModelVisibility>;
  providerVisibility: Record<string, ProviderModelVisibility>;
  providerOptionsJson: Record<string, string>;
};

export type ProviderModelCatalogItem = ProviderModelRef & {
  status?: ProviderModelStatus;
  recommended?: boolean;
  free?: boolean;
};

export type ModelCatalogItem = ProviderModelCatalogItem & {
  displayName: string;
  family?: string;
  releaseDate?: string;
  capabilities: ModelCatalogCapabilities;
  limits?: ModelCatalogLimits;
  cost?: ModelCatalogCost;
  providerOptionsSchema: ProviderConfigField[];
};

export type ModelCatalogCapabilities = {
  tools: boolean;
  reasoning: boolean;
  vision: boolean;
  audio: boolean;
  attachments: boolean;
  local: boolean;
};

export type ModelCatalogLimits = {
  context?: number;
  input?: number;
  output?: number;
};

export type ModelCatalogCost = {
  input?: number;
  output?: number;
  cacheRead?: number;
  cacheWrite?: number;
};

export type ProviderCatalogItem = {
  providerKey: string;
  displayName: string;
  documentationUrl?: string;
  capabilities: ProviderCatalogCapabilities;
  auth: ProviderAuthDescriptor;
  configurationFields: ProviderConfigField[];
};

export type ProviderCatalogCapabilities = {
  streaming: boolean;
  toolCalling: boolean;
  vision: boolean;
  audio: boolean;
};

export type ProviderAuthDescriptor = {
  kind: string;
  required: boolean;
  sources: string[];
};

export type ProviderConfigField = {
  key: string;
  label: string;
  kind: string;
  required: boolean;
  description?: string;
  options?: string[];
};

export type ProviderStatusSource = "local" | "environment" | "configuration" | "missing" | "unknown";

export type ProviderStatus = {
  providerKey: string;
  connected: boolean;
  source: ProviderStatusSource;
  removable: boolean;
  hasLocalCredential: boolean;
  message?: string;
};

export type ProviderDetail = {
  provider: ProviderCatalogItem;
  status: ProviderStatus;
};

export type ProviderCredentialRequest = {
  value: string;
  secretName?: string;
};

export type ModelPickerRow =
  | { kind: "section"; label: string }
  | {
      kind: "model";
      providerKey: string;
      modelId: string;
      providerName: string;
      displayName: string;
      label: string;
      status: "ready" | "missingCredential" | "offline" | "unknown";
      statusLabel: string;
      badges: string[];
      favorite: boolean;
      recent: boolean;
    };

export type ProviderModelStorage = {
  load(): ProviderModelUiState;
  save(state: ProviderModelUiState): void;
  hydrate?(): Promise<ProviderModelUiState>;
};

export type ProviderCatalogLoader = () => Promise<ProviderCatalogItem[]>;
export type ModelCatalogLoader = () => Promise<ModelCatalogItem[]>;

export const providerModelSettingsSource = "hpdos.chat.providerModel.v1";
export const hpdosProviderCatalogEndpoint = "/api/hpdos/providers";
export const hpdosModelCatalogEndpoint = "/api/hpdos/models";
export const modelCatalogTagOptions: ModelCatalogTagOption[] = [
  { tag: "tools", label: "Tools" },
  { tag: "reasoning", label: "Reasoning" },
  { tag: "vision", label: "Vision" },
  { tag: "free", label: "Free" },
  { tag: "local", label: "Local" }
];

const defaultRecentLimit = 12;

export function defaultProviderModelUiState(): ProviderModelUiState {
  return {
    recent: [],
    favorites: [],
    visibility: {},
    providerVisibility: {},
    providerOptionsJson: {}
  };
}

export function normalizeProviderModelUiState(value: unknown): ProviderModelUiState {
  if (typeof value !== "object" || value === null) {
    return defaultProviderModelUiState();
  }

  const record = value as Partial<ProviderModelUiState>;

  return {
    selected: normalizeSelection(record.selected),
    recent: normalizeRefs(record.recent),
    favorites: normalizeRefs(record.favorites),
    visibility: normalizeVisibility(record.visibility),
    providerVisibility: normalizeVisibility(record.providerVisibility),
    providerOptionsJson: normalizeProviderOptionsJson(record.providerOptionsJson)
  };
}

export function selectProviderModel(
  state: ProviderModelUiState,
  selection: ProviderModelSelection,
  recentLimit = defaultRecentLimit
): ProviderModelUiState {
  const normalized = normalizeSelection(selection);
  if (!normalized) return cloneProviderModelUiState(state);

  return {
    ...cloneProviderModelUiState(state),
    selected: normalized,
    recent: limitRefs([normalized, ...state.recent], recentLimit)
  };
}

export function setFavoriteProviderModel(
  state: ProviderModelUiState,
  model: ProviderModelRef,
  favorite: boolean
): ProviderModelUiState {
  const normalized = normalizeRef(model);
  if (!normalized) return cloneProviderModelUiState(state);

  const favorites = favorite
    ? limitRefs([normalized, ...state.favorites], Number.POSITIVE_INFINITY)
    : state.favorites.filter((item) => !sameProviderModel(item, normalized));

  return {
    ...cloneProviderModelUiState(state),
    favorites
  };
}

export function setModelVisibility(
  state: ProviderModelUiState,
  model: ProviderModelRef,
  visibility: ProviderModelVisibility | null
): ProviderModelUiState {
  const normalized = normalizeRef(model);
  if (!normalized) return cloneProviderModelUiState(state);

  return {
    ...cloneProviderModelUiState(state),
    visibility: setVisibility(state.visibility, modelKey(normalized), visibility)
  };
}

export function setProviderVisibility(
  state: ProviderModelUiState,
  providerKey: string,
  visibility: ProviderModelVisibility | null
): ProviderModelUiState {
  const normalizedProvider = normalizeString(providerKey);
  if (!normalizedProvider) return cloneProviderModelUiState(state);

  return {
    ...cloneProviderModelUiState(state),
    providerVisibility: setVisibility(state.providerVisibility, normalizedProvider, visibility)
  };
}

export function setProviderOptionsJson(
  state: ProviderModelUiState,
  providerKey: string,
  providerOptionsJson: string | null
): ProviderModelUiState {
  const next = cloneProviderModelUiState(state);
  const normalizedProvider = normalizeString(providerKey);
  if (!normalizedProvider) return next;

  const normalized = normalizeString(providerOptionsJson);
  if (normalized) {
    next.providerOptionsJson[normalizedProvider] = normalized;
  } else {
    delete next.providerOptionsJson[normalizedProvider];
  }

  return next;
}

export function isProviderModelVisible(
  state: ProviderModelUiState,
  model: ProviderModelCatalogItem
): boolean {
  const normalized = normalizeRef(model);
  if (!normalized) return false;

  const providerVisibility = state.providerVisibility[normalized.providerKey];
  if (providerVisibility === "hidden") return false;

  const explicitModelVisibility = state.visibility[modelKey(normalized)];
  if (explicitModelVisibility === "hidden") return false;
  if (explicitModelVisibility === "visible") return true;
  if (providerVisibility === "visible") return true;

  if (containsRef(state.favorites, normalized)) return true;
  if (containsRef(state.recent, normalized)) return true;
  if (model.recommended) return true;

  return model.status !== "deprecated" && model.status !== "alpha";
}

export function createRunConfigForProviderModel(
  selection: ProviderModelSelection | undefined,
  providerOptionsJson: Record<string, string> = {},
  base: RunConfig = {}
): RunConfig {
  if (!selection) return { ...base };

  const selectedProviderOptionsJson = providerOptionsJson[selection.providerKey];

  return {
    ...base,
    providerKey: selection.providerKey ?? base.providerKey,
    modelId: selection.modelId ?? base.modelId,
    providerOptionsJson: selectedProviderOptionsJson ?? base.providerOptionsJson
  };
}

export function formatProviderModelLabel(selection: ProviderModelSelection | ProviderModelRef | undefined): string {
  if (!selection) return "Choose a model";

  return `${selection.providerKey} / ${selection.modelId}`;
}

export function formatModelCatalogLabel(model: ModelCatalogItem | ProviderModelRef): string {
  return "displayName" in model && model.displayName
    ? `${model.providerKey} / ${model.displayName}`
    : formatProviderModelLabel(model);
}

export async function fetchHpdosProviderCatalog(
  fetchImpl: typeof fetch = globalThis.fetch,
  endpoint = hpdosProviderCatalogEndpoint
): Promise<ProviderCatalogItem[]> {
  const response = await fetchImpl(endpoint, {
    method: "GET",
    headers: { "Content-Type": "application/json" }
  });

  if (!response.ok) {
    const text = await response.text().catch(() => "Unknown error");
    throw new Error(`Failed to list HPD-OS providers: HTTP ${response.status}: ${text}`);
  }

  return normalizeProviderCatalog(await response.json());
}

export async function fetchHpdosModelCatalog(
  fetchImpl: typeof fetch = globalThis.fetch,
  endpoint = hpdosModelCatalogEndpoint
): Promise<ModelCatalogItem[]> {
  const response = await fetchImpl(endpoint, {
    method: "GET",
    headers: { "Content-Type": "application/json" }
  });

  if (!response.ok) {
    const text = await response.text().catch(() => "Unknown error");
    throw new Error(`Failed to list HPD-OS models: HTTP ${response.status}: ${text}`);
  }

  return normalizeModelCatalog(await response.json());
}

export async function fetchHpdosProviderDetail(
  providerKey: string,
  fetchImpl: typeof fetch = globalThis.fetch,
  endpointRoot = hpdosProviderCatalogEndpoint
): Promise<ProviderDetail> {
  const response = await fetchImpl(`${endpointRoot}/${encodeURIComponent(providerKey)}`, {
    method: "GET",
    headers: { "Content-Type": "application/json" }
  });

  if (!response.ok) {
    const text = await response.text().catch(() => "Unknown error");
    throw new Error(`Failed to get HPD-OS provider detail: HTTP ${response.status}: ${text}`);
  }

  return normalizeProviderDetail(await response.json());
}

export async function fetchHpdosProviderStatus(
  providerKey: string,
  fetchImpl: typeof fetch = globalThis.fetch,
  endpointRoot = hpdosProviderCatalogEndpoint
): Promise<ProviderStatus> {
  const response = await fetchImpl(`${endpointRoot}/${encodeURIComponent(providerKey)}/status`, {
    method: "GET",
    headers: { "Content-Type": "application/json" }
  });

  if (!response.ok) {
    const text = await response.text().catch(() => "Unknown error");
    throw new Error(`Failed to get HPD-OS provider status: HTTP ${response.status}: ${text}`);
  }

  return normalizeProviderStatus(await response.json());
}

export async function saveHpdosProviderCredential(
  providerKey: string,
  credential: ProviderCredentialRequest,
  fetchImpl: typeof fetch = globalThis.fetch,
  endpointRoot = hpdosProviderCatalogEndpoint
): Promise<ProviderStatus> {
  const response = await fetchImpl(`${endpointRoot}/${encodeURIComponent(providerKey)}/credential`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(credential)
  });

  if (!response.ok) {
    const text = await response.text().catch(() => "Unknown error");
    throw new Error(`Failed to save HPD-OS provider credential: HTTP ${response.status}: ${text}`);
  }

  return normalizeProviderStatus(await response.json());
}

export async function deleteHpdosProviderCredential(
  providerKey: string,
  secretName?: string,
  fetchImpl: typeof fetch = globalThis.fetch,
  endpointRoot = hpdosProviderCatalogEndpoint
): Promise<ProviderStatus> {
  const suffix = secretName ? `?secretName=${encodeURIComponent(secretName)}` : "";
  const response = await fetchImpl(`${endpointRoot}/${encodeURIComponent(providerKey)}/credential${suffix}`, {
    method: "DELETE",
    headers: { "Content-Type": "application/json" }
  });

  if (!response.ok) {
    const text = await response.text().catch(() => "Unknown error");
    throw new Error(`Failed to delete HPD-OS provider credential: HTTP ${response.status}: ${text}`);
  }

  return normalizeProviderStatus(await response.json());
}

export function normalizeProviderCatalog(value: unknown): ProviderCatalogItem[] {
  if (!Array.isArray(value)) return [];

  return value
    .map(normalizeProviderCatalogItem)
    .filter((item): item is ProviderCatalogItem => item !== undefined)
    .sort((left, right) => left.displayName.localeCompare(right.displayName));
}

export function normalizeProviderDetail(value: unknown): ProviderDetail {
  if (typeof value !== "object" || value === null) {
    return {
      provider: normalizeProviderCatalogItem(undefined) ?? missingProviderCatalogItem("unknown"),
      status: normalizeProviderStatus(undefined)
    };
  }

  const record = value as Partial<ProviderDetail>;
  return {
    provider: normalizeProviderCatalogItem(record.provider) ?? missingProviderCatalogItem("unknown"),
    status: normalizeProviderStatus(record.status)
  };
}

export function normalizeProviderStatus(value: unknown): ProviderStatus {
  if (typeof value !== "object" || value === null) {
    return {
      providerKey: "",
      connected: false,
      source: "missing",
      removable: false,
      hasLocalCredential: false
    };
  }

  const record = value as Partial<ProviderStatus>;
  const source = normalizeProviderStatusSource(record.source);
  return {
    providerKey: normalizeString(record.providerKey) ?? "",
    connected: record.connected === true,
    source,
    removable: record.removable === true,
    hasLocalCredential: record.hasLocalCredential === true,
    message: normalizeString(record.message)
  };
}

export function normalizeModelCatalog(value: unknown): ModelCatalogItem[] {
  if (!Array.isArray(value)) return [];

  return value
    .map(normalizeModelCatalogItem)
    .filter((item): item is ModelCatalogItem => item !== undefined)
    .sort((left, right) => {
      const providerCompare = left.providerKey.localeCompare(right.providerKey);
      return providerCompare !== 0
        ? providerCompare
        : left.displayName.localeCompare(right.displayName);
    });
}

export function visibleModelCatalog(state: ProviderModelUiState, models: ModelCatalogItem[]): ModelCatalogItem[] {
  return models.filter((model) => isProviderModelVisible(state, model));
}

export function buildModelPickerRows(
  state: ProviderModelUiState,
  models: ModelCatalogItem[],
  statuses: Record<string, ProviderStatus> = {},
  filter: string | ModelCatalogFilter = ""
): ModelPickerRow[] {
  const filtered = visibleModelCatalog(state, models)
    .filter((model) => providerIsSelectable(statuses[model.providerKey]))
    .filter((model) => modelMatchesFilter(model, filter));
  const rows: ModelPickerRow[] = [];
  const emitted = new Set<string>();

  const recentModels = state.recent
    .map((recent) => filtered.find((model) => sameProviderModel(model, recent)))
    .filter((model): model is ModelCatalogItem => model !== undefined);
  appendModelSection(rows, "Recent", recentModels, state, statuses, emitted);

  const favoriteModels = filtered.filter((model) => containsRef(state.favorites, model));
  appendModelSection(rows, "Favorites", favoriteModels, state, statuses, emitted);

  const recommendedModels = filtered.filter((model) => model.recommended === true);
  appendModelSection(rows, "Recommended", recommendedModels, state, statuses, emitted);

  appendModelSection(rows, "Other", filtered, state, statuses, emitted);
  return rows;
}

export function modelMatchesFilter(model: ModelCatalogItem, filter: string | ModelCatalogFilter): boolean {
  const normalizedFilter = normalizeModelCatalogFilter(filter);
  if (!modelMatchesQuery(model, normalizedFilter.query ?? "")) return false;

  return normalizedFilter.tags.every((tag) => modelHasTag(model, tag));
}

export function modelHasTag(model: ModelCatalogItem, tag: ModelCatalogTag): boolean {
  switch (tag) {
    case "tools":
      return model.capabilities.tools;
    case "reasoning":
      return model.capabilities.reasoning;
    case "vision":
      return model.capabilities.vision;
    case "free":
      return model.free === true;
    case "local":
      return model.capabilities.local;
  }
}

export function toggleModelCatalogTag(tags: ModelCatalogTag[], tag: ModelCatalogTag): ModelCatalogTag[] {
  return tags.includes(tag)
    ? tags.filter((item) => item !== tag)
    : [...tags, tag];
}

function normalizeModelCatalogFilter(filter: string | ModelCatalogFilter): Required<ModelCatalogFilter> {
  if (typeof filter === "string") {
    return {
      query: filter,
      tags: []
    };
  }

  return {
    query: filter.query ?? "",
    tags: (filter.tags ?? []).filter(isModelCatalogTag)
  };
}

function isModelCatalogTag(value: unknown): value is ModelCatalogTag {
  return value === "tools"
    || value === "reasoning"
    || value === "vision"
    || value === "free"
    || value === "local";
}

function providerIsSelectable(status: ProviderStatus | undefined): boolean {
  return status?.connected === true;
}

function normalizeSelection(value: unknown): ProviderModelSelection | undefined {
  if (typeof value !== "object" || value === null) return undefined;

  const record = value as Partial<ProviderModelSelection>;
  return normalizeRef(record);
}

function appendModelSection(
  rows: ModelPickerRow[],
  label: string,
  models: ModelCatalogItem[],
  state: ProviderModelUiState,
  statuses: Record<string, ProviderStatus>,
  emitted: Set<string>
): void {
  const sectionRows = models
    .filter((model) => !emitted.has(modelKey(model)))
    .map((model) => toModelPickerRow(model, state, statuses));

  if (sectionRows.length === 0) return;

  rows.push({ kind: "section", label });
  rows.push(...sectionRows);
  for (const row of sectionRows) {
    emitted.add(modelKey(row));
  }
}

function toModelPickerRow(
  model: ModelCatalogItem,
  state: ProviderModelUiState,
  statuses: Record<string, ProviderStatus>
): Extract<ModelPickerRow, { kind: "model" }> {
  const providerStatus = statuses[model.providerKey];
  const status = modelPickerStatus(providerStatus);

  return {
    kind: "model",
    providerKey: model.providerKey,
    modelId: model.modelId,
    providerName: model.providerKey,
    displayName: model.displayName,
    label: formatModelCatalogLabel(model),
    status,
    statusLabel: modelPickerStatusLabel(providerStatus),
    badges: modelBadges(model),
    favorite: containsRef(state.favorites, model),
    recent: containsRef(state.recent, model)
  };
}

function modelPickerStatus(status: ProviderStatus | undefined): Extract<ModelPickerRow, { kind: "model" }>["status"] {
  if (!status) return "unknown";
  if (status.connected) return "ready";
  return status.source === "missing" ? "missingCredential" : "offline";
}

function modelPickerStatusLabel(status: ProviderStatus | undefined): string {
  if (!status) return "unknown";
  if (status.connected) return status.source;
  return status.source === "missing" ? "missing key" : status.source;
}

function modelBadges(model: ModelCatalogItem): string[] {
  const badges: string[] = [];
  if (model.capabilities.reasoning) badges.push("reasoning");
  if (model.capabilities.tools) badges.push("tools");
  if (model.capabilities.vision) badges.push("vision");
  if (model.capabilities.local) badges.push("local");
  if (model.free) badges.push("free");
  if ((model.limits?.context ?? 0) >= 100000) badges.push("high context");
  if (model.status === "deprecated") badges.push("deprecated");
  return badges;
}

function modelMatchesQuery(model: ModelCatalogItem, query: string): boolean {
  const normalized = query.trim().toLowerCase();
  if (!normalized) return true;

  return model.providerKey.toLowerCase().includes(normalized)
    || model.modelId.toLowerCase().includes(normalized)
    || model.displayName.toLowerCase().includes(normalized)
    || (model.family?.toLowerCase().includes(normalized) ?? false);
}

function normalizeRefs(value: unknown): ProviderModelRef[] {
  if (!Array.isArray(value)) return [];
  return limitRefs(value.map(normalizeRef).filter((item): item is ProviderModelRef => item !== undefined), Number.POSITIVE_INFINITY);
}

function normalizeRef(value: unknown): ProviderModelRef | undefined {
  if (typeof value !== "object" || value === null) return undefined;

  const record = value as Partial<ProviderModelRef>;
  const providerKey = normalizeString(record.providerKey);
  const modelId = normalizeString(record.modelId);
  if (!providerKey || !modelId) return undefined;

  return { providerKey, modelId };
}

function normalizeVisibility(value: unknown): Record<string, ProviderModelVisibility> {
  if (typeof value !== "object" || value === null) return {};

  const result: Record<string, ProviderModelVisibility> = {};
  for (const [key, visibility] of Object.entries(value)) {
    if (visibility === "visible" || visibility === "hidden") {
      result[key] = visibility;
    }
  }
  return result;
}

function normalizeProviderOptionsJson(value: unknown): Record<string, string> {
  if (typeof value !== "object" || value === null) return {};

  const result: Record<string, string> = {};
  for (const [key, json] of Object.entries(value)) {
    const providerKey = normalizeString(key);
    const providerOptionsJson = normalizeString(json);
    if (providerKey && providerOptionsJson) {
      result[providerKey] = providerOptionsJson;
    }
  }

  return result;
}

function normalizeString(value: unknown): string | undefined {
  return typeof value === "string" && value.trim().length > 0
    ? value.trim()
    : undefined;
}

function normalizeProviderCatalogItem(value: unknown): ProviderCatalogItem | undefined {
  if (typeof value !== "object" || value === null) return undefined;

  const record = value as Partial<ProviderCatalogItem>;
  const providerKey = normalizeString(record.providerKey);
  const displayName = normalizeString(record.displayName) ?? providerKey;
  if (!providerKey || !displayName) return undefined;

  return {
    providerKey,
    displayName,
    documentationUrl: normalizeString(record.documentationUrl),
    capabilities: normalizeProviderCapabilities(record.capabilities),
    auth: normalizeProviderAuth(record.auth),
    configurationFields: normalizeProviderConfigFields(record.configurationFields)
  };
}

function missingProviderCatalogItem(providerKey: string): ProviderCatalogItem {
  return {
    providerKey,
    displayName: providerKey,
    capabilities: {
      streaming: false,
      toolCalling: false,
      vision: false,
      audio: false
    },
    auth: {
      kind: "unknown",
      required: false,
      sources: []
    },
    configurationFields: []
  };
}

function normalizeModelCatalogItem(value: unknown): ModelCatalogItem | undefined {
  if (typeof value !== "object" || value === null) return undefined;

  const record = value as Partial<ModelCatalogItem>;
  const ref = normalizeRef(record);
  const displayName = normalizeString(record.displayName) ?? ref?.modelId;
  if (!ref || !displayName) return undefined;

  return {
    ...ref,
    displayName,
    family: normalizeString(record.family),
    releaseDate: normalizeString(record.releaseDate),
    status: normalizeModelStatus(record.status),
    recommended: record.recommended === true,
    free: record.free === true,
    capabilities: normalizeModelCapabilities(record.capabilities),
    limits: normalizeModelLimits(record.limits),
    cost: normalizeModelCost(record.cost),
    providerOptionsSchema: normalizeProviderConfigFields(record.providerOptionsSchema)
  };
}

function normalizeProviderCapabilities(value: unknown): ProviderCatalogCapabilities {
  if (typeof value !== "object" || value === null) {
    return {
      streaming: false,
      toolCalling: false,
      vision: false,
      audio: false
    };
  }

  const record = value as Partial<ProviderCatalogCapabilities>;
  return {
    streaming: record.streaming === true,
    toolCalling: record.toolCalling === true,
    vision: record.vision === true,
    audio: record.audio === true
  };
}

function normalizeModelCapabilities(value: unknown): ModelCatalogCapabilities {
  if (typeof value !== "object" || value === null) {
    return {
      tools: false,
      reasoning: false,
      vision: false,
      audio: false,
      attachments: false,
      local: false
    };
  }

  const record = value as Partial<ModelCatalogCapabilities>;
  return {
    tools: record.tools === true,
    reasoning: record.reasoning === true,
    vision: record.vision === true,
    audio: record.audio === true,
    attachments: record.attachments === true,
    local: record.local === true
  };
}

function normalizeModelLimits(value: unknown): ModelCatalogLimits | undefined {
  if (typeof value !== "object" || value === null) return undefined;

  const record = value as Partial<ModelCatalogLimits>;
  const limits: ModelCatalogLimits = {};
  if (isPositiveNumber(record.context)) limits.context = record.context;
  if (isPositiveNumber(record.input)) limits.input = record.input;
  if (isPositiveNumber(record.output)) limits.output = record.output;

  return Object.keys(limits).length > 0 ? limits : undefined;
}

function normalizeModelCost(value: unknown): ModelCatalogCost | undefined {
  if (typeof value !== "object" || value === null) return undefined;

  const record = value as Partial<ModelCatalogCost>;
  const cost: ModelCatalogCost = {};
  if (isNonNegativeNumber(record.input)) cost.input = record.input;
  if (isNonNegativeNumber(record.output)) cost.output = record.output;
  if (isNonNegativeNumber(record.cacheRead)) cost.cacheRead = record.cacheRead;
  if (isNonNegativeNumber(record.cacheWrite)) cost.cacheWrite = record.cacheWrite;

  return Object.keys(cost).length > 0 ? cost : undefined;
}

function normalizeModelStatus(value: unknown): ProviderModelStatus | undefined {
  return value === "active" || value === "beta" || value === "alpha" || value === "deprecated"
    ? value
    : undefined;
}

function normalizeProviderStatusSource(value: unknown): ProviderStatusSource {
  return value === "local"
    || value === "environment"
    || value === "configuration"
    || value === "missing"
    || value === "unknown"
    ? value
    : "unknown";
}

function normalizeProviderAuth(value: unknown): ProviderAuthDescriptor {
  if (typeof value !== "object" || value === null) {
    return {
      kind: "unknown",
      required: false,
      sources: []
    };
  }

  const record = value as Partial<ProviderAuthDescriptor>;
  return {
    kind: normalizeString(record.kind) ?? "unknown",
    required: record.required === true,
    sources: Array.isArray(record.sources)
      ? record.sources.map(normalizeString).filter((item): item is string => item !== undefined)
      : []
  };
}

function normalizeProviderConfigFields(value: unknown): ProviderConfigField[] {
  if (!Array.isArray(value)) return [];

  return value
    .map((item): ProviderConfigField | undefined => {
      if (typeof item !== "object" || item === null) return undefined;

      const record = item as Partial<ProviderConfigField>;
      const key = normalizeString(record.key);
      const label = normalizeString(record.label);
      const kind = normalizeString(record.kind);
      if (!key || !label || !kind) return undefined;

      return {
        key,
        label,
        kind,
        required: record.required === true,
        description: normalizeString(record.description),
        options: Array.isArray(record.options)
          ? record.options.map(normalizeString).filter((item): item is string => item !== undefined)
          : undefined
      };
    })
    .filter((item): item is ProviderConfigField => item !== undefined);
}

function isPositiveNumber(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value) && value > 0;
}

function isNonNegativeNumber(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value) && value >= 0;
}

function cloneProviderModelUiState(state: ProviderModelUiState): ProviderModelUiState {
  return {
    selected: state.selected ? { ...state.selected } : undefined,
    recent: state.recent.map((item) => ({ ...item })),
    favorites: state.favorites.map((item) => ({ ...item })),
    visibility: { ...state.visibility },
    providerVisibility: { ...state.providerVisibility },
    providerOptionsJson: { ...state.providerOptionsJson }
  };
}

function setVisibility(
  source: Record<string, ProviderModelVisibility>,
  key: string,
  visibility: ProviderModelVisibility | null
): Record<string, ProviderModelVisibility> {
  const next = { ...source };
  if (visibility === null) {
    delete next[key];
  } else {
    next[key] = visibility;
  }
  return next;
}

function limitRefs(refs: ProviderModelRef[], limit: number): ProviderModelRef[] {
  const result: ProviderModelRef[] = [];
  for (const ref of refs) {
    if (containsRef(result, ref)) continue;
    result.push({ providerKey: ref.providerKey, modelId: ref.modelId });
    if (result.length >= limit) break;
  }
  return result;
}

function containsRef(refs: ProviderModelRef[], ref: ProviderModelRef): boolean {
  return refs.some((item) => sameProviderModel(item, ref));
}

function sameProviderModel(left: ProviderModelRef, right: ProviderModelRef): boolean {
  return left.providerKey === right.providerKey && left.modelId === right.modelId;
}

function modelKey(model: ProviderModelRef): string {
  return `${model.providerKey}:${model.modelId}`;
}
