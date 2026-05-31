<script lang="ts">
  import { tick } from "svelte";
  import type { ChatSessionState } from "../runtime/chatSession.svelte";
  import type { ProviderModelState } from "../runtime/providerModelState.svelte";
  import {
    buildModelPickerRows,
    modelCatalogTagOptions,
    toggleModelCatalogTag,
    type ModelCatalogTag,
    type ModelPickerRow
  } from "../runtime/providerModel";

  type Props = {
    session: ChatSessionState | null;
    providerModels: ProviderModelState;
    previewLabel?: string | null;
    previewText?: string | null;
    previewExpandedText?: string | null;
    streaming?: boolean;
    previewCollapsed?: boolean;
    placeholder?: string;
    onTogglePreview?: (() => void) | null;
  };

  let {
    session,
    providerModels,
    previewLabel = null,
    previewText = null,
    previewExpandedText = null,
    streaming = false,
    previewCollapsed = false,
    placeholder = "Ask HPD-Agent...",
    onTogglePreview = null
  }: Props = $props();
  let text = $state("");
  let pickerOpen = $state(false);
  let modelSearch = $state("");
  let modelTags = $state<ModelCatalogTag[]>([]);
  let modelError = $state<string | null>(null);
  let activeModelKey = $state<string | null>(null);
  let searchInput: HTMLInputElement | undefined = $state();

  const selectedModelLabel = $derived(
    providerModels.selectedModel
      ? providerModels.selectedModel.displayName
      : providerModels.selected?.modelId ?? "Choose model"
  );
  const canSend = $derived(Boolean(session)
    && Boolean(session?.workspace)
    && !session?.branchRunning
    && !session?.submitting
    && text.trim().length > 0
    && providerModels.hydrated
    && Boolean(providerModels.selected));
  const pickerRows = $derived(buildModelPickerRows(
    providerModels.state,
    providerModels.models,
    providerModels.providerStatuses,
    { query: modelSearch, tags: modelTags }
  ));
  const modelRows = $derived(pickerRows.filter((row): row is Extract<ModelPickerRow, { kind: "model" }> => row.kind === "model"));
  const activeModel = $derived(modelRows.find((row) => rowKey(row) === activeModelKey) ?? modelRows[0]);

  $effect(() => {
    if (!pickerOpen) return;
    if (modelRows.length === 0) {
      activeModelKey = null;
      return;
    }

    if (!activeModelKey || !modelRows.some((row) => rowKey(row) === activeModelKey)) {
      activeModelKey = initialActiveModelKey();
    }
  });

  async function submit(): Promise<void> {
    const value = text.trim();
    if (!value || !session || session.branchRunning || session.submitting) return;

    if (!providerModels.hydrated) {
      modelError = "Model preferences are still loading.";
      return;
    }

    if (!providerModels.selected) {
      modelError = "Choose a model before sending.";
      pickerOpen = true;
      activeModelKey = initialActiveModelKey();
      await tick();
      searchInput?.focus();
      return;
    }

    text = "";
    modelError = null;
    pickerOpen = false;
    modelSearch = "";
    modelTags = [];
    await session.sendText(value, providerModels.createRunConfig());
  }

  async function togglePicker(): Promise<void> {
    pickerOpen = !pickerOpen;
    if (!pickerOpen) return;

    activeModelKey = initialActiveModelKey();
    await tick();
    searchInput?.focus();
  }

  function selectModel(model: Extract<ModelPickerRow, { kind: "model" }>): void {
    providerModels.select({
      providerKey: model.providerKey,
      modelId: model.modelId
    });
    modelError = null;
    pickerOpen = false;
    modelSearch = "";
    modelTags = [];
  }

  function toggleFavorite(model: Extract<ModelPickerRow, { kind: "model" }>): void {
    providerModels.setFavorite(
      {
        providerKey: model.providerKey,
        modelId: model.modelId
      },
      !model.favorite
    );
  }

  function closePicker(): void {
    pickerOpen = false;
    modelSearch = "";
    modelTags = [];
    activeModelKey = null;
  }

  function toggleTag(tag: ModelCatalogTag): void {
    modelTags = toggleModelCatalogTag(modelTags, tag);
  }

  function initialActiveModelKey(): string | null {
    const selected = providerModels.selected;
    if (selected) {
      const selectedRow = modelRows.find((row) => row.providerKey === selected.providerKey && row.modelId === selected.modelId);
      if (selectedRow) return rowKey(selectedRow);
    }

    return modelRows[0] ? rowKey(modelRows[0]) : null;
  }

  function rowKey(row: Extract<ModelPickerRow, { kind: "model" }>): string {
    return `${row.providerKey}:${row.modelId}`;
  }

  function rowId(row: Extract<ModelPickerRow, { kind: "model" }>): string {
    return `hpd-chat-model-option-${row.providerKey.replace(/[^a-zA-Z0-9_-]/g, "-")}-${row.modelId.replace(/[^a-zA-Z0-9_-]/g, "-")}`;
  }

  function moveActiveModel(delta: number): void {
    if (modelRows.length === 0) return;

    const currentIndex = Math.max(0, modelRows.findIndex((row) => rowKey(row) === activeModelKey));
    const nextIndex = (currentIndex + delta + modelRows.length) % modelRows.length;
    activeModelKey = rowKey(modelRows[nextIndex]);
  }

  function handlePickerKeydown(event: KeyboardEvent): void {
    if (event.key === "Escape") {
      event.preventDefault();
      closePicker();
      return;
    }

    if (event.key === "ArrowDown") {
      event.preventDefault();
      moveActiveModel(1);
      return;
    }

    if (event.key === "ArrowUp") {
      event.preventDefault();
      moveActiveModel(-1);
      return;
    }

    if (event.key === "Home") {
      event.preventDefault();
      if (modelRows[0]) activeModelKey = rowKey(modelRows[0]);
      return;
    }

    if (event.key === "End") {
      event.preventDefault();
      const last = modelRows[modelRows.length - 1];
      if (last) activeModelKey = rowKey(last);
      return;
    }

    if (event.key === "Enter" && activeModel) {
      event.preventDefault();
      selectModel(activeModel);
    }
  }

  function handleInputKeydown(event: KeyboardEvent): void {
    if (event.key !== "Enter") return;
    if (event.shiftKey || event.altKey || event.metaKey || event.ctrlKey || event.isComposing) return;

    event.preventDefault();
    void submit();
  }
</script>

<form
  class="hpd-chat-composer"
  aria-label="Message composer"
  onsubmit={(event) => {
    event.preventDefault();
    void submit();
  }}
>
  {#if modelError}
    <p class="hpd-chat-composer-warning" role="alert">{modelError}</p>
  {/if}

  {#if pickerOpen}
    <div
      id="hpd-chat-model-picker"
      class="hpd-chat-model-picker"
      role="listbox"
      aria-label="Model picker"
      aria-activedescendant={activeModel ? rowId(activeModel) : undefined}
      tabindex="-1"
      onkeydown={handlePickerKeydown}
    >
      <label>
        <span class="sr-only">Search models</span>
        <input
          bind:this={searchInput}
          bind:value={modelSearch}
          type="search"
          placeholder="Search models..."
          aria-controls="hpd-chat-model-picker"
          aria-activedescendant={activeModel ? rowId(activeModel) : undefined}
        />
      </label>

      <div class="hpd-chat-model-tags" aria-label="Model capability filters">
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

      {#each pickerRows as row (`${row.kind}:${row.kind === "section" ? row.label : `${row.providerKey}:${row.modelId}`}`)}
        {#if row.kind === "section"}
          <p>{row.label}</p>
        {:else}
          <div class="hpd-chat-model-row">
            <button
              id={rowId(row)}
              type="button"
              role="option"
              aria-selected={providerModels.selected?.providerKey === row.providerKey && providerModels.selected?.modelId === row.modelId}
              data-active={activeModelKey === rowKey(row)}
              onclick={() => selectModel(row)}
            >
              <span>{row.label}</span>
              <small>{row.statusLabel}</small>
            </button>
            <button
              class="hpd-chat-model-favorite"
              type="button"
              aria-label={row.favorite ? `Remove ${row.label} from favorites` : `Favorite ${row.label}`}
              aria-pressed={row.favorite}
              title={row.favorite ? "Remove favorite" : "Favorite model"}
              onclick={(event) => {
                event.stopPropagation();
                toggleFavorite(row);
              }}
            >
              ★
            </button>
          </div>
        {/if}
      {/each}

      {#if pickerRows.length === 0 && providerModels.modelCatalogError}
        <p>{providerModels.modelCatalogError}</p>
      {:else if pickerRows.length === 0 && providerModels.providerCatalogError}
        <p>{providerModels.providerCatalogError}</p>
      {:else if pickerRows.length === 0 && providerModels.providers.length > 0}
        <p>No visible models. Providers are loaded from HPD-OS.</p>
      {:else if pickerRows.length === 0}
        <p>No models available yet.</p>
      {/if}
    </div>
  {/if}

  <div class="hpd-chat-composer-layout" data-has-preview={Boolean(previewText || streaming)}>
    <div class="hpd-chat-composer-box">
      <label class="sr-only" for="hpd-chat-composer-input">Message</label>
      <textarea
        id="hpd-chat-composer-input"
        bind:value={text}
        rows="3"
        {placeholder}
        disabled={!session || session.branchRunning || session.submitting}
        onkeydown={handleInputKeydown}
      ></textarea>
      <div class="hpd-chat-composer-dock">
        <button
          class="hpd-chat-model-pill"
          type="button"
          aria-haspopup="listbox"
          aria-expanded={pickerOpen}
          aria-controls="hpd-chat-model-picker"
          onclick={() => {
            void togglePicker();
          }}
        >
          <span>{selectedModelLabel}</span>
        </button>
        <button class="hpd-chat-send-button" type="submit" disabled={!canSend}>
          Send
        </button>
      </div>
    </div>

    {#if previewText || streaming}
      <section
        class="hpd-chat-agent-preview"
        aria-label="Latest agent output"
        data-streaming={streaming}
      >
        <div class="hpd-chat-agent-preview-copy">
          <strong>{previewLabel ?? (streaming ? "Working" : "Agent")}</strong>
          {#if previewText}
            <div class="hpd-chat-agent-preview-text">{previewExpandedText ?? previewText}</div>
          {:else}
            <div class="hpd-chat-agent-preview-text">Agent is working</div>
          {/if}
          {#if streaming}
            <div class="hpd-chat-agent-preview-working" role="status" aria-label="Agent is working">
              <span>Streaming</span>
              <span class="hpd-chat-loading-dots" aria-hidden="true">
                <i></i>
                <i></i>
                <i></i>
              </span>
            </div>
          {/if}
        </div>
        {#if onTogglePreview}
          <button
            class="hpd-chat-agent-preview-button"
            type="button"
            aria-label={previewCollapsed ? "Expand chat to see full agent output" : "Collapse chat transcript"}
            aria-controls="chatShell"
            aria-expanded={!previewCollapsed}
            title={previewCollapsed ? "Expand chat" : "Collapse chat"}
            onclick={() => onTogglePreview?.()}
          >
            <svg aria-hidden="true" viewBox="0 0 24 24" fill="none">
              <rect x="4" y="5" width="16" height="14" rx="2" />
              <path d="M9 5V19" />
              {#if previewCollapsed}
                <path d="M14 9L17 12L14 15" />
              {:else}
                <path d="M16 9L13 12L16 15" />
              {/if}
            </svg>
          </button>
        {/if}
      </section>
    {/if}
  </div>
</form>
