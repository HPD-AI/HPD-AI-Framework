<script lang="ts">
  import MarkdownText from "./MarkdownText.svelte";

  type Props = {
    text: string;
    complete: boolean;
  };

  let { text, complete }: Props = $props();
  let open = $state(false);

  const heading = $derived(extractReasoningHeading(text));
  const status = $derived(complete ? "Thought" : "Thinking");
  const body = $derived(stripReasoningHeading(text, heading).trim());

  function extractReasoningHeading(markdown: string): string | undefined {
    const normalized = markdown.replace(/\r\n?/g, "\n").trim();
    if (!normalized) return undefined;

    const html = normalized.match(/<h[1-6][^>]*>([\s\S]*?)<\/h[1-6]>/i);
    if (html?.[1]) return cleanHeading(html[1].replace(/<[^>]+>/g, " "));

    const atx = normalized.match(/^\s{0,3}#{1,6}[ \t]+(.+?)(?:[ \t]+#+[ \t]*)?$/m);
    if (atx?.[1]) return cleanHeading(atx[1]);

    const setext = normalized.match(/^([^\n]+)\n(?:=+|-+)\s*$/m);
    if (setext?.[1]) return cleanHeading(setext[1]);

    const strong = normalized.match(/^\s*(?:\*\*|__)(.+?)(?:\*\*|__)\s*(?:\n|$)/);
    if (strong?.[1]) return cleanHeading(strong[1]);

    return undefined;
  }

  function stripReasoningHeading(markdown: string, title: string | undefined): string {
    if (!title) return markdown;

    return markdown
      .replace(/^\s{0,3}#{1,6}[ \t]+.+?(?:[ \t]+#+[ \t]*)?\r?\n+/, "")
      .replace(/^([^\n]+)\r?\n(?:=+|-+)\s*\r?\n+/, "")
      .replace(/^\s*(?:\*\*|__)(.+?)(?:\*\*|__)\s*(?:\r?\n\r?\n|\r?\n|$)/, "")
      .trimStart();
  }

  function cleanHeading(value: string): string | undefined {
    const cleaned = value
      .replace(/`([^`]+)`/g, "$1")
      .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1")
      .replace(/[*_~]+/g, "")
      .trim();

    return cleaned.length > 0 ? cleaned : undefined;
  }
</script>

{#if text.trim().length > 0 || !complete}
  <details class="hpd-chat-reasoning" bind:open>
    <summary class="hpd-chat-reasoning-summary">
      <span class="hpd-chat-reasoning-state">{open ? "-" : "+"}</span>
      <span>{status}</span>
      {#if heading}
        <span class="hpd-chat-reasoning-title">{heading}</span>
      {/if}
    </summary>

    {#if body.length > 0}
      <div class="hpd-chat-reasoning-body">
        <MarkdownText text={body} />
      </div>
    {/if}
  </details>
{/if}
