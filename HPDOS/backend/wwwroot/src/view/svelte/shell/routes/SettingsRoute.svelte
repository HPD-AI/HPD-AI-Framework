<script lang="ts">
  import type { ChatRuntimeController } from "../../chat/runtime/chatRuntime.svelte";
  import {
    deleteHpdosProviderCredential,
    formatModelCatalogLabel,
    modelCatalogTagOptions,
    modelMatchesFilter,
    saveHpdosProviderCredential,
    toggleModelCatalogTag,
    type ModelCatalogTag,
    type ModelCatalogItem,
    type ProviderConfigField,
    type ProviderCatalogItem,
    type ProviderStatus
  } from "../../chat/runtime/providerModel";

  type Props = {
    chatRuntime: ChatRuntimeController;
  };

  let { chatRuntime }: Props = $props();

  const providerModels = $derived(chatRuntime.providerModels);
  const providers = $derived(providerModels.providers);
  const models = $derived(providerModels.models);
  const statuses = $derived(providerModels.providerStatuses);
  const selectedProviderKey = $state<{ value: string | null }>({ value: null });
  let credentialValue = $state("");
  let providerActionMessage = $state<string | null>(null);
  type ProviderOptionValue = string | number | boolean | string[];

  let providerOptionsValues = $state<Record<string, ProviderOptionValue>>({});
  let providerOptionsError = $state<string | null>(null);
  let modelSearch = $state("");
  let modelTags = $state<ModelCatalogTag[]>([]);
  let requestedSettingsCatalogLoad = false;

  const selectedProvider = $derived(
    providers.find((provider) => provider.providerKey === selectedProviderKey.value)
      ?? providers[0]
      ?? null
  );
  const selectedProviderStatus = $derived(
    selectedProvider ? statuses[selectedProvider.providerKey] : undefined
  );
  const selectedProviderModels = $derived(
    selectedProvider
      ? models.filter((model) => model.providerKey === selectedProvider.providerKey)
      : []
  );
  const modelSections = $derived(buildModelSections(selectedProviderModels, modelSearch));
  const selectedSecretField = $derived(
    selectedProvider?.configurationFields.find((field) => field.kind === "secret") ?? null
  );
  const unsupportedProviderFields = $derived(
    selectedProvider?.configurationFields.filter((field) => !supportedProviderField(field)) ?? []
  );
  const selectedProviderOptionFields = $derived(
    selectedProviderModels.find((model) => model.providerOptionsSchema.length > 0)?.providerOptionsSchema ?? []
  );
  const selectedProviderOptionsJson = $derived(
    selectedProvider ? providerModels.state.providerOptionsJson[selectedProvider.providerKey] : undefined
  );

  $effect(() => {
    providerOptionsValues = parseProviderOptions(selectedProviderOptionsJson);
    providerOptionsError = null;
  });

  $effect(() => {
    if (requestedSettingsCatalogLoad) return;
    if (providers.length > 0 && models.length > 0) return;

    requestedSettingsCatalogLoad = true;
    void providerModels.loadCatalogs();
  });

  function providerStatus(provider: ProviderCatalogItem): ProviderStatus | undefined {
    return statuses[provider.providerKey];
  }

  function modelIsHidden(model: ModelCatalogItem): boolean {
    return providerModels.state.visibility[`${model.providerKey}:${model.modelId}`] === "hidden";
  }

  function providerIsHidden(provider: ProviderCatalogItem): boolean {
    return providerModels.state.providerVisibility[provider.providerKey] === "hidden";
  }

  function providerName(providerKey: string): string {
    return providers.find((provider) => provider.providerKey === providerKey)?.displayName ?? providerKey;
  }

  async function saveCredential(): Promise<void> {
    if (!selectedProvider || credentialValue.trim().length === 0) return;

    providerActionMessage = null;
    const status = await saveHpdosProviderCredential(selectedProvider.providerKey, {
      value: credentialValue,
      secretName: selectedSecretField?.key
    });
    providerModels.providerStatuses = {
      ...providerModels.providerStatuses,
      [selectedProvider.providerKey]: status
    };
    credentialValue = "";
    providerActionMessage = `${selectedProvider.displayName} credential saved.`;
  }

  async function removeCredential(): Promise<void> {
    if (!selectedProvider) return;

    providerActionMessage = null;
    const status = await deleteHpdosProviderCredential(selectedProvider.providerKey, selectedSecretField?.key);
    providerModels.providerStatuses = {
      ...providerModels.providerStatuses,
      [selectedProvider.providerKey]: status
    };
    providerActionMessage = `${selectedProvider.displayName} local credential removed.`;
  }

  function applyProviderOptions(): void {
    providerOptionsError = null;
    const next: Record<string, unknown> = {};

    for (const field of selectedProviderOptionFields) {
      const value = providerOptionsValues[field.key];
      if (value === undefined || value === "" || value === false) continue;
      if (Array.isArray(value) && value.length === 0) continue;

      if (field.kind === "number") {
        const numberValue = typeof value === "number" ? value : Number(value);
        if (!Number.isFinite(numberValue)) {
          providerOptionsError = `${field.label} must be a number.`;
          return;
        }

        next[field.key] = numberValue;
        continue;
      }

      if (field.kind === "boolean") {
        next[field.key] = value === true;
        continue;
      }

      if (field.kind === "json") {
        try {
          next[field.key] = JSON.parse(String(value));
        } catch {
          providerOptionsError = `${field.label} must be valid JSON.`;
          return;
        }

        continue;
      }

      if (field.kind === "multiSelect") {
        next[field.key] = Array.isArray(value) ? value : [String(value)];
        continue;
      }

      next[field.key] = String(value);
    }

    const json = Object.keys(next).length > 0 ? JSON.stringify(next) : null;
    if (!selectedProvider) return;

    providerModels.setProviderOptionsJson(selectedProvider.providerKey, json);
    providerActionMessage = json ? `${selectedProvider.displayName} provider options saved.` : "Provider options cleared.";
  }

  function parseProviderOptions(value: string | undefined): Record<string, ProviderOptionValue> {
    if (!value) return {};

    try {
      const parsed = JSON.parse(value);
      if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
        return {};
      }

      const result: Record<string, ProviderOptionValue> = {};
      for (const [key, item] of Object.entries(parsed)) {
        if (typeof item === "string" || typeof item === "number" || typeof item === "boolean") {
          result[key] = item;
        } else if (Array.isArray(item) && item.every((entry) => typeof entry === "string")) {
          result[key] = item;
        } else {
          result[key] = JSON.stringify(item, null, 2);
        }
      }

      return result;
    } catch {
      return {};
    }
  }

  function providerOptionValue(field: ProviderConfigField): ProviderOptionValue {
    if (field.kind === "multiSelect") {
      const value = providerOptionsValues[field.key];
      return Array.isArray(value) ? value : [];
    }

    return providerOptionsValues[field.key] ?? (field.kind === "boolean" ? false : "");
  }

  function setProviderOptionValue(field: ProviderConfigField, value: ProviderOptionValue): void {
    providerOptionsValues = {
      ...providerOptionsValues,
      [field.key]: value
    };
  }

  function toggleProviderOptionValue(field: ProviderConfigField, option: string, selected: boolean): void {
    const current = providerOptionValue(field);
    const values = Array.isArray(current) ? current : [];
    setProviderOptionValue(
      field,
      selected
        ? [...values.filter((value) => value !== option), option]
        : values.filter((value) => value !== option)
    );
  }

  function providerOptionSelected(field: ProviderConfigField, option: string): boolean {
    const current = providerOptionValue(field);
    return Array.isArray(current) && current.includes(option);
  }

  function clearProviderOptions(): void {
    if (!selectedProvider) return;

    providerOptionsValues = {};
    providerModels.setProviderOptionsJson(selectedProvider.providerKey, null);
    providerOptionsError = null;
  }

  function inputTypeForField(field: ProviderConfigField): string {
    if (field.kind === "url") return "url";
    if (field.kind === "number") return "number";
    return "text";
  }

  function providerOptionPlaceholder(field: ProviderConfigField): string {
    if (field.kind === "json") return "{}";
    if (field.kind === "number") return "Optional number";
    if (field.kind === "url") return "https://...";
    return "Optional";
  }

  function providerOptionAria(field: ProviderConfigField): string | undefined {
    return field.description ? `hpd-provider-option-help-${field.key}` : undefined;
  }

  function supportedProviderOptionField(field: ProviderConfigField): boolean {
    return field.kind === "text"
      || field.kind === "url"
      || field.kind === "number"
      || field.kind === "boolean"
      || field.kind === "select"
      || field.kind === "multiSelect"
      || field.kind === "json";
  }

  function unsupportedProviderOptionFieldCount(fields: ProviderConfigField[]): number {
    return fields.filter((field) => !supportedProviderOptionField(field)).length;
  }

  function supportedProviderField(field: ProviderConfigField): boolean {
    return field.kind === "secret";
  }

  type ModelSection = {
    label: string;
    models: ModelCatalogItem[];
  };

  function buildModelSections(
    catalog: ModelCatalogItem[],
    query: string
  ): ModelSection[] {
    const filtered = catalog.filter((model) => modelMatchesFilter(model, { query, tags: modelTags }));
    const sections: ModelSection[] = [];
    const emitted = new Set<string>();

    appendModelSection(
      sections,
      "Recommended",
      filtered.filter((model) => model.recommended === true && !isModelDeprecated(model)),
      emitted
    );
    appendModelSection(sections, "Hidden", filtered.filter(isModelHiddenInSettings), emitted);
    appendModelSection(sections, "Deprecated", filtered.filter(isModelDeprecated), emitted);
    appendModelSection(sections, "All models", filtered, emitted);

    return sections;
  }

  function appendModelSection(
    sections: ModelSection[],
    label: string,
    candidates: ModelCatalogItem[],
    emitted: Set<string>
  ): void {
    const sectionModels = candidates.filter((model) => {
      const key = modelKey(model);
      if (emitted.has(key)) return false;
      emitted.add(key);
      return true;
    });

    if (sectionModels.length > 0) {
      sections.push({ label, models: sectionModels });
    }
  }

  function isModelHiddenInSettings(model: ModelCatalogItem): boolean {
    return providerModels.state.visibility[modelKey(model)] === "hidden"
      || providerModels.state.providerVisibility[model.providerKey] === "hidden";
  }

  function isModelDeprecated(model: ModelCatalogItem): boolean {
    return model.status === "deprecated";
  }

  function modelKey(model: ModelCatalogItem): string {
    return `${model.providerKey}:${model.modelId}`;
  }

  function toggleTag(tag: ModelCatalogTag): void {
    modelTags = toggleModelCatalogTag(modelTags, tag);
  }
</script>

<section class="hpd-page-route" aria-label="Settings">
  <div class="hpd-page-route-strip"></div>
  <section class="hpd-page-route-body hpd-settings-route">
    <header class="hpd-settings-header">
      <h1>Settings</h1>
      <p>Providers and models are configured here. The chat composer only chooses what to run.</p>
    </header>

    <section class="hpd-settings-grid" aria-label="Provider and model settings">
      <section class="hpd-settings-panel" aria-labelledby="hpd-provider-settings-heading">
        <div class="hpd-settings-panel-header">
          <h2 id="hpd-provider-settings-heading">Providers</h2>
          <button type="button" onclick={() => providerModels.loadCatalogs()}>Refresh</button>
        </div>

        <div class="hpd-settings-provider-list" role="listbox" aria-label="Providers">
          {#each providers as provider (provider.providerKey)}
            {@const status = providerStatus(provider)}
            <button
              type="button"
              role="option"
              aria-selected={selectedProvider?.providerKey === provider.providerKey}
              onclick={() => {
                selectedProviderKey.value = provider.providerKey;
                providerActionMessage = null;
              }}
            >
              <span>{provider.displayName}</span>
              <small data-status={status?.connected ? "connected" : "missing"}>
                {status?.source ?? "missing"}
              </small>
            </button>
          {/each}
        </div>

        {#if selectedProvider}
          <section class="hpd-settings-detail" aria-label={`${selectedProvider.displayName} provider settings`}>
            <div>
              <h3>{selectedProvider.displayName}</h3>
              <p>{selectedProviderStatus?.message ?? (selectedProviderStatus?.connected ? "Connected" : "Missing credential")}</p>
            </div>

            <dl>
              <div>
                <dt>Credential source</dt>
                <dd>{selectedProviderStatus?.source ?? "missing"}</dd>
              </div>
              <div>
                <dt>Auth</dt>
                <dd>{selectedProvider.auth.kind}</dd>
              </div>
              <div>
                <dt>Stored locally</dt>
                <dd>{selectedProviderStatus?.hasLocalCredential ? "yes" : "no"}</dd>
              </div>
            </dl>

            {#if selectedSecretField}
              <label>
                <span>{selectedSecretField.label}</span>
                <input
                  bind:value={credentialValue}
                  type="password"
                  autocomplete="off"
                  placeholder={selectedProviderStatus?.hasLocalCredential ? "Replace stored credential" : "Add credential"}
                  aria-describedby={selectedSecretField.description ? "hpd-provider-secret-help" : undefined}
                />
                {#if selectedSecretField.description}
                  <small id="hpd-provider-secret-help">{selectedSecretField.description}</small>
                {/if}
              </label>
              <div class="hpd-settings-actions">
                <button type="button" disabled={credentialValue.trim().length === 0} onclick={() => void saveCredential()}>
                  Save credential
                </button>
                <button type="button" disabled={!selectedProviderStatus?.removable} onclick={() => void removeCredential()}>
                  Remove local credential
                </button>
              </div>
            {/if}

            {#if unsupportedProviderFields.length > 0}
              <p class="hpd-settings-message">
                {unsupportedProviderFields.length} provider field{unsupportedProviderFields.length === 1 ? "" : "s"} need a newer settings renderer.
              </p>
            {/if}

            {#if providerActionMessage}
              <p class="hpd-settings-message">{providerActionMessage}</p>
            {/if}
          </section>
        {/if}
      </section>

      <section class="hpd-settings-panel" aria-labelledby="hpd-model-settings-heading">
        <div class="hpd-settings-panel-header">
          <h2 id="hpd-model-settings-heading">Models</h2>
          <span>
            {#if selectedProvider}
              {selectedProviderModels.length} {selectedProvider.displayName} models
            {:else}
              No provider selected
            {/if}
          </span>
        </div>

        <label class="hpd-settings-search">
          <span class="sr-only">Search provider models</span>
          <input
            bind:value={modelSearch}
            type="search"
            placeholder={selectedProvider ? `Search ${selectedProvider.displayName} models...` : "Search models..."}
          />
        </label>

        <div class="hpd-settings-model-tags" aria-label="Model capability filters">
          {#each modelCatalogTagOptions as option (option.tag)}
            <button
              type="button"
              aria-pressed={modelTags.includes(option.tag)}
              onclick={() => toggleTag(option.tag)}
            >
              {option.label}
            </button>
          {/each}
        </div>

        {#if selectedProvider}
          <label class="hpd-settings-toggle">
            <input
              type="checkbox"
              checked={!providerIsHidden(selectedProvider)}
              onchange={(event) => {
                providerModels.setProviderVisibility(
                  selectedProvider.providerKey,
                  event.currentTarget.checked ? null : "hidden"
                );
              }}
            />
            <span>Show {selectedProvider.displayName} models in picker</span>
          </label>
        {/if}

        {#if selectedProvider}
          <section class="hpd-settings-options" aria-label={`${selectedProvider.displayName} provider options`}>
            <div>
              <h3>Provider options</h3>
              <p>{selectedProvider.displayName} options are attached to runs that use this provider.</p>
            </div>

            {#if selectedProviderOptionFields.length > 0}
              <div class="hpd-settings-option-grid">
                {#each selectedProviderOptionFields as field (field.key)}
                  {#if supportedProviderOptionField(field)}
                    <label>
                      <span>{field.label}</span>
                      {#if field.kind === "boolean"}
                        <input
                          type="checkbox"
                          checked={providerOptionValue(field) === true}
                          onchange={(event) => setProviderOptionValue(field, event.currentTarget.checked)}
                        />
                      {:else if field.kind === "select"}
                        <select
                          value={String(providerOptionValue(field))}
                          onchange={(event) => setProviderOptionValue(field, event.currentTarget.value)}
                        >
                          <option value="">Default</option>
                          {#each field.options ?? [] as option}
                            <option value={option}>{option}</option>
                          {/each}
                        </select>
                      {:else if field.kind === "multiSelect"}
                        <div class="hpd-settings-option-checks">
                          {#each field.options ?? [] as option}
                            <label>
                              <input
                                type="checkbox"
                                checked={providerOptionSelected(field, option)}
                                onchange={(event) => toggleProviderOptionValue(field, option, event.currentTarget.checked)}
                              />
                              <span>{option}</span>
                            </label>
                          {/each}
                        </div>
                      {:else if field.kind === "json"}
                        <textarea
                          value={String(providerOptionValue(field))}
                          rows="4"
                          spellcheck="false"
                          placeholder={providerOptionPlaceholder(field)}
                          aria-describedby={providerOptionAria(field)}
                          oninput={(event) => setProviderOptionValue(field, event.currentTarget.value)}
                        ></textarea>
                      {:else}
                        <input
                          value={String(providerOptionValue(field))}
                          type={inputTypeForField(field)}
                          placeholder={providerOptionPlaceholder(field)}
                          aria-describedby={providerOptionAria(field)}
                          oninput={(event) => setProviderOptionValue(field, event.currentTarget.value)}
                        />
                      {/if}

                      {#if field.description}
                        <small id={`hpd-provider-option-help-${field.key}`}>{field.description}</small>
                      {/if}
                    </label>
                  {/if}
                {/each}
              </div>

              {#if unsupportedProviderOptionFieldCount(selectedProviderOptionFields) > 0}
                <p class="hpd-settings-message">
                  {unsupportedProviderOptionFieldCount(selectedProviderOptionFields)} provider option field{unsupportedProviderOptionFieldCount(selectedProviderOptionFields) === 1 ? "" : "s"} need a newer settings renderer.
                </p>
              {/if}
            {:else}
              <p class="hpd-settings-message">This provider does not expose run options.</p>
            {/if}

            <div class="hpd-settings-actions">
              <button type="button" onclick={applyProviderOptions}>Apply options</button>
              <button
                type="button"
                disabled={Object.keys(providerOptionsValues).length === 0 && !selectedProviderOptionsJson}
                onclick={clearProviderOptions}
              >
                Clear options
              </button>
            </div>
            {#if providerOptionsError}
              <p class="hpd-settings-message">{providerOptionsError}</p>
            {/if}
          </section>
        {/if}

        <div class="hpd-settings-model-list">
          {#each modelSections as section (section.label)}
            <section class="hpd-settings-model-section" aria-label={section.label}>
              <h3>{section.label}</h3>
              {#each section.models as model (`${model.providerKey}:${model.modelId}`)}
                <article class="hpd-settings-model-row">
                  <div>
                    <h4>{formatModelCatalogLabel(model)}</h4>
                    <p>
                      {providerName(model.providerKey)}
                      {#if model.capabilities.reasoning} · reasoning{/if}
                      {#if model.capabilities.tools} · tools{/if}
                      {#if model.capabilities.vision} · vision{/if}
                      {#if model.capabilities.local} · local{/if}
                      {#if model.free} · free{/if}
                      {#if model.status} · {model.status}{/if}
                    </p>
                  </div>
                  <div class="hpd-settings-model-actions">
                    <label>
                      <input
                        type="checkbox"
                        checked={!modelIsHidden(model)}
                        onchange={(event) => {
                          providerModels.setModelVisibility(model, event.currentTarget.checked ? null : "hidden");
                        }}
                      />
                      <span>Visible</span>
                    </label>
                  </div>
                </article>
              {/each}
            </section>
          {:else}
            <p class="hpd-settings-empty">No matching models.</p>
          {/each}
        </div>
      </section>
    </section>
  </section>
</section>
