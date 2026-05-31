<script lang="ts">
  import { renderMarkdown } from "../runtime/markdown";

  type Props = {
    text: string;
  };

  let { text }: Props = $props();
  let html = $state("");
  let failed = $state(false);

  $effect(() => {
    let active = true;
    failed = false;

    renderMarkdown(text)
      .then((rendered) => {
        if (active) {
          html = rendered;
        }
      })
      .catch(() => {
        if (active) {
          failed = true;
          html = "";
        }
      });

    return () => {
      active = false;
    };
  });
</script>

<div class="hpd-chat-markdown">
  {#if failed}
    <p>{text}</p>
  {:else}
    {@html html}
  {/if}
</div>
