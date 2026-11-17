<script lang="ts">
  interface Option {
    readonly label: string;
    readonly value: string | number;
  }

  interface Props {
    label: string;
    type?: "text" | "number" | "boolean" | "select" | "list" | "multiline" | "json";
    value: unknown;
    options?: readonly Option[];
    min?: number;
    max?: number;
    step?: number;
    placeholder?: string;
    hint?: string;
    onchange: (value: unknown) => void;
  }

  let {
    label,
    type = "text",
    value,
    options = [],
    min,
    max,
    step,
    placeholder,
    hint,
    onchange,
  }: Props = $props();

  function text(value: unknown): string {
    return typeof value === "string" ? value : "";
  }

  function numberText(value: unknown): string {
    return typeof value === "number" && Number.isFinite(value) ? String(value) : "";
  }

  function listText(value: unknown): string {
    return Array.isArray(value) ? value.map(item => text(item)).join(", ") : "";
  }

  function setNumber(valueText: string): void {
    if (valueText.trim() === "") {
      onchange(undefined);
      return;
    }
    const parsed = Number(valueText);
    if (Number.isFinite(parsed)) onchange(parsed);
  }

  function jsonText(value: unknown): string {
    return value === undefined ? "" : JSON.stringify(value, null, 2);
  }

  function setJson(valueText: string): void {
    if (valueText.trim() === "") {
      onchange(undefined);
      return;
    }
    try {
      onchange(JSON.parse(valueText));
    } catch {
      // Keep the editable text intact until the user completes valid JSON.
    }
  }
</script>

<label class="grid content-start gap-1">
  <span class="studio-label">{label}</span>
  {#if type === "boolean"}
    <span class="flex min-h-9 items-center gap-2 rounded-studio border border-studio-line bg-studio-panel px-3 text-body-sm">
      <input type="checkbox" checked={value === true} onchange={event => onchange(event.currentTarget.checked)} />
      <span>{value === true ? "Enabled" : "Disabled"}</span>
    </span>
  {:else if type === "select"}
    <select class="studio-input" value={String(value ?? "")}
      onchange={event => {
        const selected = options.find(option => String(option.value) === event.currentTarget.value);
        onchange(selected?.value);
      }}>
      <option value="">Not configured</option>
      {#each options as option (String(option.value))}<option value={String(option.value)}>{option.label}</option>{/each}
    </select>
  {:else if type === "number"}
    <input class="studio-input" type="number" value={numberText(value)}
      {min} {max} {step} {placeholder} oninput={event => setNumber(event.currentTarget.value)} />
  {:else if type === "list"}
    <input class="studio-input font-mono" value={listText(value)} {placeholder}
      oninput={event => onchange(event.currentTarget.value.split(",").map(item => item.trim()).filter(Boolean))} />
  {:else if type === "multiline"}
    <textarea class="studio-input min-h-24" value={text(value)} {placeholder}
      oninput={event => onchange(event.currentTarget.value)}></textarea>
  {:else if type === "json"}
    <textarea class="studio-input min-h-36 font-mono text-code" spellcheck="false"
      value={jsonText(value)} {placeholder} oninput={event => setJson(event.currentTarget.value)}></textarea>
  {:else}
    <input class="studio-input" value={text(value)} {placeholder}
      oninput={event => onchange(event.currentTarget.value)} />
  {/if}
  {#if hint}<span class="text-meta text-studio-muted">{hint}</span>{/if}
</label>
