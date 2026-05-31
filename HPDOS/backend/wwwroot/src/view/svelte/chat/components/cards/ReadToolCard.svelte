<script lang="ts">
  import type { ToolCallItem as ToolCallCardItem } from "../../runtime/chatTypes";
  import ToolCardShell from "./ToolCardShell.svelte";

  type Props = {
    item: ToolCallCardItem;
  };

  let { item }: Props = $props();

  const args = $derived(readArgs(item.args));
  const path = $derived(args.filePath ?? args.path);
  const title = $derived("Read");
  const subtitle = $derived(path ? basename(path) : item.status);
</script>

<ToolCardShell {item} {title} {subtitle}>
  {#if path}
    <div class="hpd-chat-tool-summary">
      <code>{path}</code>
    </div>
  {/if}
</ToolCardShell>

<script lang="ts" module>
  import type { ToolCallItem } from "../../runtime/chatTypes";

  type ReadArgs = {
    filePath?: string;
    path?: string;
  };

  function readArgs(value: unknown): ReadArgs {
    if (!isRecord(value)) return {};
    return {
      filePath: stringValue(value.filePath),
      path: stringValue(value.path)
    };
  }

  function basename(path: string): string {
    return path.split(/[\\/]/).filter(Boolean).at(-1) ?? path;
  }

  function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === "object" && value !== null && !Array.isArray(value);
  }

  function stringValue(value: unknown): string | undefined {
    return typeof value === "string" && value.trim().length > 0 ? value.trim() : undefined;
  }
</script>
