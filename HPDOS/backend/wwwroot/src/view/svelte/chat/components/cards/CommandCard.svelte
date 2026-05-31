<script lang="ts">
  import type { ToolCallItem as ToolCallCardItem } from "../../runtime/chatTypes";
  import ToolCardShell from "./ToolCardShell.svelte";

  type Props = {
    item: ToolCallCardItem;
  };

  let { item }: Props = $props();

  const command = $derived(item.command);
  const title = $derived(command?.baseCommand ?? item.name);
  const subtitle = $derived(commandSubtitle(item));
  const output = $derived(command?.liveOutput ?? item.result?.text ?? "");
  const artifactLabels = $derived(commandArtifactLabels(item));
</script>

<ToolCardShell {item} {title} {subtitle}>
  {#if command?.command}
    <div class="hpd-chat-tool-summary">
      <code>{command.command}</code>
      {#if command.workingDirectory}
        <span>{command.workingDirectory}</span>
      {/if}
      {#if command.exitCode !== undefined}
        <span>exit {command.exitCode ?? "none"}</span>
      {/if}
      {#if command.durationMilliseconds !== undefined}
        <span>{Math.round(command.durationMilliseconds)}ms</span>
      {/if}
    </div>
  {/if}

  {#if output}
    <pre class="hpd-chat-command-output"><code>{stripAnsi(output)}</code></pre>
  {:else if item.status === "running"}
    <p class="hpd-chat-tool-result-summary">Command is running.</p>
  {/if}

  {#if command && (command.outputTruncated || command.outputEventsSuppressed || artifactLabels.length > 0)}
    <div class="hpd-chat-tool-details">
      {#if command.outputTruncated}
        <span>output truncated</span>
      {/if}
      {#if command.outputEventsSuppressed}
        <span>live output suppressed</span>
      {/if}
      {#each artifactLabels as label}
        <span>{label}</span>
      {/each}
    </div>
  {/if}
</ToolCardShell>

<script lang="ts" module>
  import type { ToolCallItem } from "../../runtime/chatTypes";

  function commandSubtitle(item: ToolCallItem): string {
    const command = item.command;
    if (!command) return item.status;
    const parts: string[] = [item.status];
    if (command.shell) parts.push(command.shell);
    if (command.completionKind) parts.push(command.completionKind);
    return parts.join(" · ");
  }

  function commandArtifactLabels(item: ToolCallItem): string[] {
    const artifacts = item.command?.artifacts;
    if (!artifacts) return [];

    const labels: string[] = [];
    if (artifacts.stdoutArtifactPath || artifacts.stdoutContentId || artifacts.stdoutLocalPath) labels.push("stdout artifact");
    if (artifacts.stderrArtifactPath || artifacts.stderrContentId || artifacts.stderrLocalPath) labels.push("stderr artifact");
    if (artifacts.combinedOutputArtifactPath || artifacts.combinedOutputContentId || artifacts.combinedOutputLocalPath) labels.push("combined output artifact");
    return labels;
  }

  function stripAnsi(value: string): string {
    return value.replace(/\u001b\[[0-9;?]*[ -/]*[@-~]/g, "");
  }
</script>
