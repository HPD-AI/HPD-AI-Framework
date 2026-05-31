<script lang="ts">
  import type { ToolCallItem } from "../../runtime/chatTypes";
  import CommandCard from "./CommandCard.svelte";
  import FileMutationCard from "./FileMutationCard.svelte";
  import ListToolCard from "./ListToolCard.svelte";
  import ReadToolCard from "./ReadToolCard.svelte";
  import UnknownToolCard from "./UnknownToolCard.svelte";

  type Props = {
    item: ToolCallItem;
  };

  let { item }: Props = $props();

  const toolName = $derived(item.name.toLowerCase());
</script>

{#if item.command}
  <CommandCard {item} />
{:else if item.fileMutation}
  <FileMutationCard {item} />
{:else if toolName === "read" || toolName === "readfile"}
  <ReadToolCard {item} />
{:else if toolName === "list" || toolName === "ls" || toolName === "listfiles"}
  <ListToolCard {item} />
{:else}
  <UnknownToolCard {item} />
{/if}
