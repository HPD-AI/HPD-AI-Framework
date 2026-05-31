<script lang="ts">
  import type { ToolCallItem as ToolCallCardItem } from "../../runtime/chatTypes";
  import ToolCardShell from "./ToolCardShell.svelte";

  type Props = {
    item: ToolCallCardItem;
  };

  let { item }: Props = $props();

  const mutation = $derived(item.fileMutation);
  const title = $derived(fileMutationTitle(item));
  const subtitle = $derived(mutation ? `${mutation.type} · ${mutation.changed ? "changed" : "no change"}` : item.status);
  const badge = $derived(mutation?.diffStat ? `+${mutation.diffStat.addedLines} -${mutation.diffStat.removedLines}` : undefined);
</script>

<ToolCardShell {item} {title} {subtitle} {badge}>
  {#if mutation}
    <div class="hpd-chat-tool-summary">
      <code>{mutation.displayPath}</code>
      {#if mutation.mutationKind}
        <span>{mutation.mutationKind}</span>
      {/if}
      {#if mutation.created}
        <span>created</span>
      {/if}
      {#if mutation.editCount !== undefined}
        <span>{mutation.editCount} edits</span>
      {/if}
      {#if mutation.replacementCount !== undefined}
        <span>{mutation.replacementCount} replacements</span>
      {/if}
    </div>

    {#if mutation.hunks?.length}
      <div class="hpd-chat-diff">
        {#each mutation.hunks as hunk}
          <div class="hpd-chat-diff-hunk">
            <div class="hpd-chat-diff-hunk-header">
              @@ -{hunk.oldStart},{hunk.oldLines} +{hunk.newStart},{hunk.newLines} @@
            </div>
            {#each hunk.lines as line}
              <pre data-line-kind={lineKind(line)}>{line}</pre>
            {/each}
          </div>
        {/each}
      </div>
    {:else if item.result?.text}
      <p class="hpd-chat-tool-result-summary">{item.result.text}</p>
    {/if}

    {#if mutation.hunksTruncated || mutation.notes?.length}
      <div class="hpd-chat-tool-details">
        {#if mutation.hunksTruncated}
          <span>diff truncated</span>
        {/if}
        {#each mutation.notes ?? [] as note}
          <span>{String(note)}</span>
        {/each}
      </div>
    {/if}
  {/if}
</ToolCardShell>

<script lang="ts" module>
  import type { ToolCallItem } from "../../runtime/chatTypes";

  function fileMutationTitle(item: ToolCallItem): string {
    const mutation = item.fileMutation;
    if (!mutation) return item.name;

    if (mutation.type === "write") {
      if (mutation.created) return `Create ${mutation.displayPath}`;
      if (mutation.mode) return `${capitalize(mutation.mode)} ${mutation.displayPath}`;
      return `Write ${mutation.displayPath}`;
    }

    return `Edit ${mutation.displayPath}`;
  }

  function lineKind(line: string): "add" | "remove" | "context" {
    if (line.startsWith("+")) return "add";
    if (line.startsWith("-")) return "remove";
    return "context";
  }

  function capitalize(value: string): string {
    return value.length === 0 ? value : value[0].toUpperCase() + value.slice(1);
  }
</script>
