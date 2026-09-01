<script lang="ts">
  import { onMount } from 'svelte';
  interface CommandTarget { readonly kind: string; readonly authorityChecksum: string }
  interface CommandHandle {
    open(commandId: string, target: never, input?: unknown): void;
    snapshot(): unknown;
    subscribe(listener: (state: unknown) => void): () => void;
    preview(signal?: AbortSignal): Promise<void>;
    acknowledge(acknowledgementId: string, accepted: boolean): void;
    execute(signal?: AbortSignal): Promise<void>;
    resolve(signal?: AbortSignal): Promise<void>;
    close(): void;
  }
  let { commandIds, target, commands }: { commandIds: readonly string[]; target: CommandTarget | null; commands: CommandHandle } = $props();
  let commandState: unknown = $state({ kind: 'closed' });
  let selected = $state('');
  let busy = $state(false);
  const kind = $derived(readKind(commandState));
  const acknowledgements = $derived(readAcknowledgements(commandState));
  onMount(() => commands.subscribe(value => commandState = value));
  function open(): void {
    if (target === null || !commandIds.includes(selected)) return;
    commands.open(selected, target as never, Object.freeze({}));
  }
  async function invoke(operation: () => Promise<void>): Promise<void> {
    if (busy) return; busy = true;
    try { await operation(); } finally { busy = false; }
  }
  function readKind(value: unknown): string {
    return value !== null && typeof value === 'object' && typeof (value as { kind?: unknown }).kind === 'string'
      ? (value as { kind: string }).kind : 'closed';
  }
  function readAcknowledgements(value: unknown): readonly {key:string;label:string}[] { if (value === null || typeof value !== 'object') return [];
    const preview = (value as { preview?: unknown }).preview; return preview !== null && typeof preview === 'object' &&
      Array.isArray((preview as { acknowledgements?: unknown }).acknowledgements)
      ? (preview as { acknowledgements: unknown[] }).acknowledgements.flatMap(item=>item!==null&&typeof item==='object'&&typeof (item as {purposeId?:unknown}).purposeId==='string'&&typeof (item as {impactId?:unknown}).impactId==='string'
        ? [{key:`${(item as {purposeId:string}).purposeId}\0${(item as {impactId:string}).impactId}`,label:`${(item as {purposeId:string}).purposeId}: ${(item as {impactId:string}).impactId}`}]:[]) : []; }
</script>

<section aria-label="Command review" class="grid gap-3">
  <div><p class="studio-label">Reviewed command</p><p class="studio-text-safe text-sm">State: {kind}</p></div>
  {#if commandIds.length === 0 || target === null}
    <p class="studio-status studio-status-info" role="status">No command is disclosed for this resource and page.</p>
  {:else}
    <label class="grid gap-1 text-sm font-semibold">Command
      <select class="studio-input" bind:value={selected}><option value="">Select a disclosed command</option>
        {#each commandIds as id (id)}<option value={id}>{id}</option>{/each}
      </select>
    </label>
    <button class="studio-button" type="button" disabled={!selected || busy} onclick={open}>Open review</button>
    {#if kind !== 'closed'}
      <button class="studio-button" type="button" disabled={busy} onclick={() => invoke(() => commands.preview())}>Preview exact request</button>
    {/if}
    {#if kind === 'review'}
      {#each acknowledgements as acknowledgement (acknowledgement.key)}
        <label class="flex gap-2"><input type="checkbox" onchange={(event)=>commands.acknowledge(acknowledgement.key,event.currentTarget.checked)}/><span>{acknowledgement.label}</span></label>
      {/each}
      <button class="studio-button studio-button-primary" type="button" disabled={busy}
        onclick={() => invoke(() => commands.execute())}>Execute reviewed command</button>
    {/if}
    {#if kind === 'retryable'}<button class="studio-button studio-button-primary" type="button" disabled={busy}
      onclick={() => invoke(() => commands.execute())}>Retry exact request</button>{/if}
    {#if kind === 'indeterminate' || kind === 'unresolved'}
      <button class="studio-button" type="button" disabled={busy} onclick={() => invoke(() => commands.resolve())}>Resolve receipt</button>
    {/if}
    {#if kind !== 'closed'}<button class="studio-button" type="button" disabled={busy} onclick={() => commands.close()}>Close</button>{/if}
  {/if}
</section>
