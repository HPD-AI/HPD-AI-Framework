import type { RunConfig } from "@hpd/hpd-agent-client";
import { createDesktopProviderModelStorage } from "../storage";
import {
  fetchHpdosModelCatalog,
  fetchHpdosProviderCatalog,
  fetchHpdosProviderStatus,
  createRunConfigForProviderModel,
  defaultProviderModelUiState,
  formatProviderModelLabel,
  selectProviderModel,
  setFavoriteProviderModel,
  setModelVisibility,
  setProviderVisibility,
  setProviderOptionsJson,
  visibleModelCatalog,
  type ModelCatalogItem,
  type ModelCatalogLoader,
  type ProviderCatalogItem,
  type ProviderCatalogLoader,
  type ProviderModelRef,
  type ProviderModelSelection,
  type ProviderModelStorage,
  type ProviderModelUiState,
  type ProviderModelVisibility,
  type ProviderStatus
} from "./providerModel";

export type ProviderModelStateOptions = {
  storage?: ProviderModelStorage;
  providerCatalogLoader?: ProviderCatalogLoader;
  modelCatalogLoader?: ModelCatalogLoader;
  providerStatusLoader?: (providerKey: string) => Promise<ProviderStatus>;
};

export class ProviderModelState {
  readonly storage: ProviderModelStorage;
  readonly providerCatalogLoader: ProviderCatalogLoader;
  readonly modelCatalogLoader: ModelCatalogLoader;
  readonly providerStatusLoader: (providerKey: string) => Promise<ProviderStatus>;

  state = $state<ProviderModelUiState>(defaultProviderModelUiState());
  providerCatalog = $state<ProviderCatalogItem[]>([]);
  modelCatalog = $state<ModelCatalogItem[]>([]);
  providerStatuses = $state<Record<string, ProviderStatus>>({});
  hydrated = $state(false);
  error = $state<string | null>(null);
  providerCatalogError = $state<string | null>(null);
  modelCatalogError = $state<string | null>(null);
  providerStatusError = $state<string | null>(null);

  selected = $derived(this.state.selected);
  recent = $derived(this.state.recent);
  providers = $derived(this.providerCatalog);
  models = $derived(this.modelCatalog);
  visibleModels = $derived(visibleModelCatalog(this.state, this.modelCatalog));
  selectedModel = $derived(this.state.selected
    ? this.modelCatalog.find((model) => model.providerKey === this.state.selected?.providerKey && model.modelId === this.state.selected?.modelId)
    : undefined);
  selectedStatus = $derived(this.state.selected ? this.providerStatuses[this.state.selected.providerKey] : undefined);
  selectedLabel = $derived(formatProviderModelLabel(this.state.selected));

  constructor(options: ProviderModelStateOptions = {}) {
    this.storage = options.storage ?? createDesktopProviderModelStorage();
    this.providerCatalogLoader = options.providerCatalogLoader ?? fetchHpdosProviderCatalog;
    this.modelCatalogLoader = options.modelCatalogLoader ?? fetchHpdosModelCatalog;
    this.providerStatusLoader = options.providerStatusLoader ?? ((providerKey) => fetchHpdosProviderStatus(providerKey));
    this.state = this.storage.load();
  }

  async hydrate(): Promise<void> {
    const catalogLoad = this.loadCatalogs();

    if (!this.storage.hydrate) {
      this.hydrated = true;
      await catalogLoad;
      return;
    }

    try {
      this.state = await this.storage.hydrate();
      this.error = null;
    } catch (error) {
      this.error = error instanceof Error ? error.message : "Failed to load provider model preferences.";
    } finally {
      this.hydrated = true;
    }

    await catalogLoad;
  }

  async loadCatalogs(): Promise<void> {
    await Promise.all([
      this.loadProviderCatalog(),
      this.loadModelCatalog()
    ]);
  }

  async loadProviderCatalog(): Promise<void> {
    try {
      this.providerCatalog = await this.providerCatalogLoader();
      this.providerCatalogError = null;
      await this.loadProviderStatuses();
    } catch (error) {
      this.providerCatalog = [];
      this.providerCatalogError = error instanceof Error ? error.message : "Failed to load provider catalog.";
    }
  }

  async loadProviderStatuses(): Promise<void> {
    const statuses: Record<string, ProviderStatus> = {};

    const results = await Promise.allSettled(this.providerCatalog.map(async (provider) => {
      statuses[provider.providerKey] = await this.providerStatusLoader(provider.providerKey);
    }));

    this.providerStatuses = statuses;

    const failed = results.find((result) => result.status === "rejected");
    if (failed && failed.status === "rejected") {
      const error = failed.reason;
      this.providerStatusError = error instanceof Error ? error.message : "Failed to load some provider statuses.";
    } else {
      this.providerStatusError = null;
    }
  }

  async loadModelCatalog(): Promise<void> {
    try {
      this.modelCatalog = await this.modelCatalogLoader();
      this.modelCatalogError = null;
    } catch (error) {
      this.modelCatalog = [];
      this.modelCatalogError = error instanceof Error ? error.message : "Failed to load model catalog.";
    }
  }

  select(selection: ProviderModelSelection): void {
    this.state = selectProviderModel(this.state, selection);
    this.storage.save(this.state);
  }

  useSessionSelection(selection: ProviderModelSelection): void {
    this.select(selection);
  }

  setFavorite(model: ProviderModelRef, favorite: boolean): void {
    this.state = setFavoriteProviderModel(this.state, model, favorite);
    this.storage.save(this.state);
  }

  setModelVisibility(model: ProviderModelRef, visibility: ProviderModelVisibility | null): void {
    this.state = setModelVisibility(this.state, model, visibility);
    this.storage.save(this.state);
  }

  setProviderVisibility(providerKey: string, visibility: ProviderModelVisibility | null): void {
    this.state = setProviderVisibility(this.state, providerKey, visibility);
    this.storage.save(this.state);
  }

  setProviderOptionsJson(providerKey: string, providerOptionsJson: string | null): void {
    this.state = setProviderOptionsJson(this.state, providerKey, providerOptionsJson);
    this.storage.save(this.state);
  }

  createRunConfig(base: RunConfig = {}): RunConfig {
    return createRunConfigForProviderModel(this.state.selected, this.state.providerOptionsJson, base);
  }
}

export function createProviderModelState(options: ProviderModelStateOptions = {}): ProviderModelState {
  const providerModels = new ProviderModelState(options);
  void providerModels.hydrate();
  return providerModels;
}
