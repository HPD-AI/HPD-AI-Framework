<script lang="ts">
  interface ResourceLink { readonly relation: string; readonly label: string; readonly target: Readonly<{ kind: string; authorityChecksum: string }> }
  let { links, onnavigate }: { links: readonly ResourceLink[]; onnavigate: (link: ResourceLink) => void | Promise<void> } = $props();
</script>
<nav aria-label="Related resources" class="grid gap-2">
  <h2 class="text-base font-bold">Related resources</h2>
  {#if links.length === 0}<p class="studio-text-safe text-sm text-studio-muted">No disclosed links.</p>
  {:else}<ul class="grid gap-1">{#each links as link (link.target.authorityChecksum + link.relation)}<li>
    <button class="studio-rail-item" type="button" onclick={() => onnavigate(link)}><span>{link.label}</span><small>{link.relation}</small></button>
  </li>{/each}</ul>{/if}
</nav>
