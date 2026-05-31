<script lang="ts">
  import type { ToolCallItem as ToolCallCardItem } from "../../runtime/chatTypes";
  import MarkdownText from "../MarkdownText.svelte";
  import ToolCardShell from "./ToolCardShell.svelte";

  type Props = {
    item: ToolCallCardItem;
  };

  let { item }: Props = $props();

  const args = $derived(listArgs(item.args));
  const path = $derived(args.path ?? args.directory ?? "/");
  const title = $derived("List");
  const subtitle = $derived(path);
  const output = $derived(item.result?.text ?? "");
</script>

<ToolCardShell {item} {title} {subtitle}>
  <div class="hpd-chat-tool-summary">
    <code>{path}</code>
    {#if args.pattern}
      <span>pattern={args.pattern}</span>
    {/if}
    {#if item.status === "running"}
      <span>running</span>
    {/if}
  </div>

  {#if output}
    <div class="hpd-chat-tool-markdown-output">
      <MarkdownText text={output} />
    </div>
  {/if}
</ToolCardShell>

<script lang="ts" module>
  type ListArgs = {
    path?: string;
    directory?: string;
    pattern?: string;
  };

  function listArgs(value: unknown): ListArgs {
    if (!isRecord(value)) return {};
    return {
      path: stringValue(value.path),
      directory: stringValue(value.directory),
      pattern: stringValue(value.pattern)
    };
  }

  function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === "object" && value !== null && !Array.isArray(value);
  }

  function stringValue(value: unknown): string | undefined {
    return typeof value === "string" && value.trim().length > 0 ? value.trim() : undefined;
  }
</script>
