<script lang="ts">
  import type { ToolCallItem as ToolCallCardItem } from "../../runtime/chatTypes";
  import ToolCardShell from "./ToolCardShell.svelte";

  type Props = {
    item: ToolCallCardItem;
  };

  let { item }: Props = $props();

  const label = $derived(firstUsefulLabel(item));
  const resultText = $derived(item.result?.text ?? "");
</script>

<ToolCardShell {item} title={item.name} subtitle={label ?? item.status}>
  {#if label}
    <div class="hpd-chat-tool-summary">
      <span>{label}</span>
    </div>
  {/if}

  {#if resultText}
    <p class="hpd-chat-tool-result-summary">{resultText}</p>
  {/if}
</ToolCardShell>

<script lang="ts" module>
  import type { ToolCallItem } from "../../runtime/chatTypes";

  const usefulArgKeys = ["description", "query", "url", "filePath", "path", "pattern", "name"];

  function firstUsefulLabel(item: ToolCallItem): string | undefined {
    if (typeof item.args === "object" && item.args !== null && !Array.isArray(item.args)) {
      const args = item.args as Record<string, unknown>;
      for (const key of usefulArgKeys) {
        const value = args[key];
        if (typeof value === "string" && value.trim().length > 0) return value.trim();
      }

      const primitives = Object.entries(args)
        .filter(([, value]) => typeof value === "string" || typeof value === "number" || typeof value === "boolean")
        .slice(0, 3)
        .map(([key, value]) => `${key}: ${String(value)}`);

      if (primitives.length > 0) return primitives.join(" · ");
    }

    if (typeof item.args === "string" && item.args.trim().length > 0) return item.args.trim();
    return undefined;
  }
</script>
