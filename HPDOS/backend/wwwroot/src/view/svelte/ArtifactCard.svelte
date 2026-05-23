<script lang="ts">
  import type { ArtifactRecord, ArtifactView } from "../../core/hpdosArtifacts.js";
  import { formatDate, jsonish } from "../../shared/format.js";
  import { markdownHtml } from "../markdown.js";
  import type { ViewActions } from "./types.js";

  let {
    artifact,
    view,
    open,
    actions
  }: {
    artifact: ArtifactRecord;
    view: ArtifactView;
    open: boolean;
    actions: ViewActions;
  } = $props();

  function artifactIcon(type: ArtifactRecord["type"]) {
    if (type === "code") return "{}";
    if (type === "markdown") return "MD";
    if (type === "html") return "<>";
    if (type === "json") return "[]";
    return "T";
  }

  function previewMarkupContent(artifact: ArtifactRecord) {
    if (artifact.type === "html") return artifact.content;
    if (artifact.type !== "code") return null;

    const language = (artifact.language || "").trim().toLowerCase();
    const contentStart = artifact.content.trimStart().toLowerCase();
    const isMarkupLanguage = language === "html"
      || language === "htm"
      || language === "svg"
      || language === "xml";
    const isRenderableMarkup = contentStart.startsWith("<!doctype html")
      || contentStart.startsWith("<html")
      || contentStart.startsWith("<svg");

    return isMarkupLanguage && isRenderableMarkup ? artifact.content : null;
  }

  let previewMarkup = $derived(previewMarkupContent(artifact));
</script>

<section class="hpd-artifact" data-open={String(open)} data-artifact-card={artifact.id}>
  <div class="hpd-artifact-header">
    <div class="min-w-0">
      <div class="hpd-inline">
        <span class="hpd-badge font-mono">{artifactIcon(artifact.type)}</span>
        <h3 class="hpd-title-sm">{artifact.title}</h3>
      </div>
      <p class="hpd-meta">{artifact.type}{artifact.language ? ` / ${artifact.language}` : ""}</p>
    </div>
    <div class="hpd-cluster">
      <div class="hpd-tab-group">
        <button class="hpd-artifact-tab" aria-current={view === "preview" ? "page" : undefined} onclick={() => actions.setArtifactView(artifact.id, "preview")} type="button">Preview</button>
        <button class="hpd-artifact-tab" aria-current={view === "code" ? "page" : undefined} onclick={() => actions.setArtifactView(artifact.id, "code")} type="button">Code</button>
      </div>
      <button class="hpd-badge" onclick={() => open ? actions.closeArtifact() : actions.openArtifact(artifact.id)} type="button">
        {open ? "Open" : "Focus"}
      </button>
      <span class="hpd-badge">{formatDate(artifact.updatedAt)}</span>
    </div>
  </div>
  <div class="hpd-artifact-body">
    {#if view === "code"}
      <div class="hpd-artifact-render">
        <pre>{artifact.type === "json" ? jsonish(artifact.content) : artifact.content}</pre>
      </div>
    {:else if artifact.type === "markdown"}
      <div class="hpd-artifact-render">{@html markdownHtml(artifact.content)}</div>
    {:else if previewMarkup !== null}
      <iframe class="hpd-artifact-frame" loading="lazy" referrerpolicy="no-referrer" sandbox="allow-scripts" srcdoc={previewMarkup} title={artifact.title}></iframe>
    {:else if artifact.type === "json"}
      <div class="hpd-artifact-render">
        <pre>{jsonish(artifact.content)}</pre>
      </div>
    {:else if artifact.type === "code"}
      <div class="hpd-artifact-render">
        <pre>{artifact.content}</pre>
      </div>
    {:else}
      <div class="hpd-artifact-render">{artifact.content}</div>
    {/if}
  </div>
</section>
