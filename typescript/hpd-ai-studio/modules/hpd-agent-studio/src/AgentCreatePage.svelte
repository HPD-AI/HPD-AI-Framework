<script lang="ts">
  import { onMount } from 'svelte';
  import {
    studioOutwardResourceChecksum,
    type StudioOutwardResourceAuthority,
    type StudioPageProps
  } from '@hpd-research/hpd-studio-core';
  import {
    StudioButton,
    StudioNotice,
    StudioPageHeader,
    StudioPanel,
    StudioStatusBadge
  } from '@hpd-research/hpd-studio-design';
  import { materializeAgentConfigYaml } from './agent-config-yaml.ts';
  import { requireAgentStudioRuntime } from './runtime-context.ts';
  import StructuredConfigurationEditor from './StructuredConfigurationEditor.svelte';

  let { resource, commands }: StudioPageProps = $props();
  const runtime = requireAgentStudioRuntime();
  type Draft = Record<string, any>;
  type SectionId = "identity" | "model" | "runtime" | "capabilities" | "reliability" | "advanced";
  type Materialization = "json" | "yaml";

  const sections: readonly { id: SectionId; label: string; summary: string }[] = Object.freeze([
    { id: "identity", label: "Identity", summary: "Agent ID, display name, and operating instructions." },
    { id: "model", label: "Model", summary: "Transport, provider reference, model, endpoint, and sampling." },
    { id: "runtime", label: "Runtime", summary: "Agent loop limits, continuation, subagents, and history behavior." },
    { id: "capabilities", label: "Capabilities", summary: "ToolHarnesses, middleware, collapsing, skills, MCP, and tool output." },
    { id: "reliability", label: "Reliability", summary: "Validation, retries, compaction, Goals, background work, audio, and shutdown." },
    { id: "advanced", label: "Advanced", summary: "Every remaining AgentConfig section with a focused JSON editor." },
  ]);
  let section: SectionId = $state("identity");
  let materialization: Materialization = $state("json");
  let agentId: string = $state("");
  let commandState: unknown = $state({ kind: "closed" });
  let accepted: boolean = $state(false);
  let busy: boolean = $state(false);
  let failure: string = $state("");
  let success: string = $state("");
  let draft: Draft = $state({
    name: "New Agent",
    systemInstructions: "You are a helpful assistant.",
    maxAgenticIterations: 10,
    maxSubAgentDepth: 4,
    continuationExtensionAmount: 3,
    allowUserQuestions: false,
    includeReasoningInModelHistory: false,
    coalesceDeltas: false,
    clients: { transport: 0 },
    defaultSubAgentForkOptions: { policy: 0, descendantPolicy: 0 },
    collapsing: {
      enabled: true,
      collapseClientTools: false,
      maxFunctionNamesInDescription: 10,
      persistSystemPromptInjections: false,
      enableErrorRecovery: true,
      recoveryHistoryMode: 1,
      hideToolHarnessInteractionsWithinTurn: true,
    },
    toolHarnesses: [],
    middlewares: [],
    skills: { instructionDelivery: 0 },
    toolOutputs: {
      enabled: true,
      maxInlineResultBytes: 65536,
      failureMode: 0,
      contentRetrievalEnabled: true,
      retrievalLimits: {
        maxListResultsPerCall: 50,
        maxReadBytesPerCall: 65536,
        maxReadBytesPerTurn: 262144,
        maxReadCallsPerTurn: 8,
        maxListCallsPerTurn: 8,
      },
    },
    shutdown: {
      gracefulDrainTimeout: "00:00:30",
      cancellationDrainTimeout: "00:00:10",
      remoteOperations: 0,
      leaseLeaks: 0,
    },
    operationRetention: {
      terminalRetention: "30.00:00:00",
      providerDeduplicationRetention: "90.00:00:00",
      maximumTerminalOperationsPerThread: 10000,
    },
  });
  let materializedConfig = $derived(prune(cloneDraft()));
  let configJson = $derived(JSON.stringify(materializedConfig, null, 2));
  let configYaml = $derived(materializeAgentConfigYaml(materializedConfig));
  let commandKind = $derived(readCommandKind(commandState));
  let preview = $derived(readCommandPreview(commandState));
  let identityReady = $derived(
    /^[A-Za-z0-9._-]{1,128}$/u.test(agentId.trim()) &&
    text(draft.name).trim().length > 0 &&
    text(draft.systemInstructions).trim().length > 0
  );

  onMount(() => {
    const unsubscribe = commands.subscribe(value => commandState = value);
    return () => {
      unsubscribe();
      commands.close();
    };
  });

  function text(value: unknown): string {
    return typeof value === "string" ? value : "";
  }

  function get(path: string): unknown {
    return path.split('.').reduce<unknown>((current, key) =>
      current !== null && typeof current === "object"
        ? (current as Record<string, unknown>)[key]
        : undefined, draft);
  }

  function set(path: string, value: unknown): void {
    const next = cloneDraft();
    const keys = path.split('.');
    let current: Record<string, any> = next;
    for (const key of keys.slice(0, -1)) {
      const child = current[key];
      current[key] = child !== null && typeof child === "object" && !Array.isArray(child)
        ? child
        : {};
      current = current[key];
    }
    const finalKey = keys.at(-1)!;
    if (value === undefined || (typeof value === "string" && value.length === 0))
      delete current[finalKey];
    else current[finalKey] = value;
    draft = next;
    resetReview();
  }

  function cloneDraft(): Draft {
    return JSON.parse(JSON.stringify(draft)) as Draft;
  }

  function setNumber(path: string, value: string, integer = true): void {
    if (value.trim() === "") {
      set(path, undefined);
      return;
    }
    const parsed = Number(value);
    if (!Number.isFinite(parsed)) return;
    set(path, integer ? Math.trunc(parsed) : parsed);
  }

  function setList(path: string, value: string): void {
    const values = value.split(',').map(item => item.trim()).filter(Boolean);
    set(path, values.length > 0 ? values : undefined);
  }

  function listText(value: unknown): string {
    return Array.isArray(value) ? value.join(", ") : "";
  }

  function numberText(value: unknown): string {
    return typeof value === "number" && Number.isFinite(value) ? String(value) : "";
  }

  function triState(value: unknown): string {
    return typeof value === "boolean" ? String(value) : "inherit";
  }

  function applicationTarget(): StudioOutwardResourceAuthority {
    if (resource?.kind === "application") return resource;
    const identity = Object.freeze({
      kind: "application" as const,
      applicationId: runtime.applicationId,
    });
    return Object.freeze({ ...identity, authorityChecksum: studioOutwardResourceChecksum(identity) });
  }

  async function reviewCreation(): Promise<void> {
    if (busy || !identityReady) return;
    busy = true;
    accepted = false;
    failure = "";
    success = "";
    resetReview(false);
    try {
      commands.open("agent.create", applicationTarget(), Object.freeze({
        agentId: agentId.trim(),
        configJson,
      }));
      await commands.preview();
      if (readCommandKind(commandState) !== "review") {
        commands.close();
        failure = "The server rejected this Agent configuration. Check required provider and capability references.";
      }
    } catch (error) {
      commands.close();
      failure = commandFailure(error, "Agent creation review failed.");
    } finally {
      busy = false;
    }
  }

  function acknowledge(value: boolean): void {
    accepted = value;
    const acknowledgement = preview?.acknowledgements[0];
    if (acknowledgement)
      commands.acknowledge(`${acknowledgement.purposeId}\0${acknowledgement.impactId}`, value);
  }

  async function createAgent(): Promise<void> {
    if (busy || commandKind !== "review" || !accepted) return;
    busy = true;
    failure = "";
    try {
      await commands.execute();
      const kind = readCommandKind(commandState);
      if (kind === "confirmed" || kind === "duplicate") {
        const result = commandState !== null && typeof commandState === "object"
          ? commandState as Record<string, unknown> : {};
        success = `${text(result.name) || text(draft.name)} (${text(result.agentId) || agentId.trim()}) was created.`;
        commands.close();
        globalThis.location.assign("/studio/operations/agents");
        return;
      }
      failure = commandFailure(kind, "Agent creation did not complete.");
    } catch (error) {
      failure = commandFailure(error, "Agent creation did not complete.");
    } finally {
      busy = false;
    }
  }

  function resetReview(close: boolean = true): void {
    accepted = false;
    if (close && readCommandKind(commandState) !== "closed" && readCommandKind(commandState) !== "unresolved")
      commands.close();
  }

  function commandFailure(error: unknown, fallback: string): string {
    const kind = typeof error === "string" ? error : readCommandKind(error);
    if (kind === "retryable") return "The host rejected creation before influence. Review the current configuration.";
    if (kind === "unresolved") return "Creation is indeterminate. Verify the Agent catalog before retrying.";
    if (kind === "failed") return "Creation failed validation or an Agent ID conflict occurred.";
    return error instanceof Error ? error.message : fallback;
  }

  function readCommandKind(value: unknown): string {
    if (value === null || typeof value !== "object") return "closed";
    const state = value as { kind?: unknown; mode?: unknown };
    if (typeof state.kind === "string") return state.kind;
    return state.mode === "execute" ? "confirmed" : "closed";
  }

  function readCommandPreview(value: unknown): {
    agentId: string; name: string; configChecksum: string; previewChecksum: string;
    expiresAtUtc: string; acknowledgements: readonly { purposeId: string; impactId: string }[];
  } | null {
    if (value === null || typeof value !== "object" || (value as { kind?: unknown }).kind !== "review")
      return null;
    const item = (value as { preview?: unknown }).preview;
    if (item === null || typeof item !== "object") return null;
    const record = item as Record<string, unknown>;
    return {
      agentId: text(record.agentId),
      name: text(record.name),
      configChecksum: text(record.configChecksum),
      previewChecksum: text(record.previewChecksum),
      expiresAtUtc: text(record.expiresAtUtc),
      acknowledgements: Array.isArray(record.acknowledgements)
        ? record.acknowledgements.flatMap(ack => ack && typeof ack === "object" &&
          typeof (ack as Record<string, unknown>).purposeId === "string" &&
          typeof (ack as Record<string, unknown>).impactId === "string"
          ? [{ purposeId: (ack as Record<string, string>).purposeId, impactId: (ack as Record<string, string>).impactId }]
          : [])
        : [],
    };
  }

  function prune(value: unknown): unknown {
    if (Array.isArray(value)) {
      const items = value.map(prune).filter(item => item !== undefined);
      return items.length > 0 ? items : undefined;
    }
    if (value !== null && typeof value === "object") {
      const result: Record<string, unknown> = {};
      for (const [key, member] of Object.entries(value as Record<string, unknown>)) {
        const pruned = prune(member);
        if (pruned !== undefined) result[key] = pruned;
      }
      return Object.keys(result).length > 0 ? result : undefined;
    }
    if (value === undefined || value === null || value === "") return undefined;
    return value;
  }
</script>

<main class="min-h-0 p-4 sm:p-6 lg:p-8">
  <div class="mx-auto grid w-full max-w-[100rem] gap-5">
    <button type="button" class="justify-self-start text-body-sm font-semibold text-studio-muted hover:text-studio-text"
      onclick={() => location.assign("/studio/operations/agents")}>← Back to Agents</button>
    <StudioPageHeader eyebrow="Agents / New definition" title="Create Agent"
      description="Compose the portable definition visually. Studio materializes the same structure as JSON or YAML and reviews it against the host authority before creation.">
      {#snippet actions()}
        <StudioStatusBadge tone="info">agent.create</StudioStatusBadge>
        <StudioButton variant="primary" type="button" loading={busy} disabled={!identityReady}
          onclick={() => void reviewCreation()}>Review Agent</StudioButton>
      {/snippet}
    </StudioPageHeader>

    {#if success}<StudioNotice tone="success" title="Agent created"><p class="studio-text-safe">{success}</p></StudioNotice>{/if}
    {#if failure}<StudioNotice tone="danger" title="Agent creation needs attention"><p class="studio-text-safe">{failure}</p></StudioNotice>{/if}

    <div class="grid gap-4 xl:grid-cols-[minmax(10rem,.55fr)_minmax(28rem,1.35fr)_minmax(22rem,.9fr)]">
      <nav class="grid content-start gap-2" aria-label="Agent configuration sections">
        {#each sections as item (item.id)}
          <button type="button" class={`rounded-studio border p-3 text-left ${
            section === item.id ? "border-studio-focus bg-studio-panel-raised" : "border-studio-line bg-studio-panel"
          }`} onclick={() => section = item.id}>
            <strong class="block text-body-sm">{item.label}</strong>
            <span class="mt-1 block text-meta text-studio-muted">{item.summary}</span>
          </button>
        {/each}
      </nav>

      <div class="grid content-start gap-4">
        {#if section === "identity"}
          <StudioPanel title="Identity and instructions" description="The Agent ID is a command input; it is not part of AgentConfig.">
            <div class="grid gap-4">
              <label class="grid gap-1"><span class="studio-label">Agent ID *</span>
                <input class="studio-input font-mono" value={agentId} maxlength="128" placeholder="research-agent"
                  oninput={event => { agentId = event.currentTarget.value; resetReview(); }} />
                <span class="text-meta text-studio-muted">1–128 ASCII letters, digits, dots, hyphens, or underscores. This becomes immutable.</span>
              </label>
              <label class="grid gap-1"><span class="studio-label">Display name *</span>
                <input class="studio-input" value={text(draft.name)} maxlength="256"
                  oninput={event => set("name", event.currentTarget.value)} />
              </label>
              <label class="grid gap-1"><span class="studio-label">Operating instructions *</span>
                <textarea class="studio-input min-h-64" value={text(draft.systemInstructions)} maxlength="65536"
                  oninput={event => set("systemInstructions", event.currentTarget.value)}></textarea>
                <span class="text-meta text-studio-muted">This is the system instruction materialized as <code>systemInstructions</code>.</span>
              </label>
            </div>
          </StudioPanel>
        {:else if section === "model"}
          <StudioPanel title="Model transport" description="Select a portable provider reference. Credentials and live client instances stay host-owned.">
            <div class="grid gap-4 md:grid-cols-2">
              <label class="grid gap-1"><span class="studio-label">Transport</span>
                <select class="studio-input" value={numberText(get("clients.transport")) || "0"}
                  onchange={event => setNumber("clients.transport", event.currentTarget.value)}>
                  <option value="0">Auto</option><option value="1">Chat</option><option value="2">Realtime</option>
                </select>
              </label>
              <label class="grid gap-1"><span class="studio-label">Provider key</span>
                <input class="studio-input font-mono" value={text(get("clients.chat.provider.key"))}
                  placeholder="provider.package.identity" oninput={event => set("clients.chat.provider.key", event.currentTarget.value)} />
              </label>
              <label class="grid gap-1"><span class="studio-label">Provider backend</span>
                <input class="studio-input font-mono" value={text(get("clients.chat.provider.backend"))}
                  placeholder="optional backend identity" oninput={event => set("clients.chat.provider.backend", event.currentTarget.value)} />
              </label>
              <label class="grid gap-1"><span class="studio-label">Model name</span>
                <input class="studio-input font-mono" value={text(get("clients.chat.modelName"))}
                  placeholder="model-id" oninput={event => set("clients.chat.modelName", event.currentTarget.value)} />
              </label>
              <label class="grid gap-1 md:col-span-2"><span class="studio-label">Endpoint override</span>
                <input class="studio-input font-mono" value={text(get("clients.chat.endpoint"))}
                  placeholder="optional provider endpoint" oninput={event => set("clients.chat.endpoint", event.currentTarget.value)} />
              </label>
              <label class="grid gap-1"><span class="studio-label">Temperature</span>
                <input class="studio-input" type="number" min="0" max="2" step="0.1" value={numberText(get("clients.chat.temperature"))}
                  oninput={event => setNumber("clients.chat.temperature", event.currentTarget.value, false)} />
              </label>
              <label class="grid gap-1"><span class="studio-label">Top P</span>
                <input class="studio-input" type="number" min="0" max="1" step="0.05" value={numberText(get("clients.chat.topP"))}
                  oninput={event => setNumber("clients.chat.topP", event.currentTarget.value, false)} />
              </label>
              <label class="grid gap-1"><span class="studio-label">Max output tokens</span>
                <input class="studio-input" type="number" min="1" value={numberText(get("clients.chat.maxOutputTokens"))}
                  oninput={event => setNumber("clients.chat.maxOutputTokens", event.currentTarget.value)} />
              </label>
            </div>
          </StudioPanel>
          <StudioNotice tone="info" title="Portable provider selection">
            <p>Use task-bound authentication references supplied by the host. Do not author API keys, tokens, or provider client objects in this document.</p>
          </StudioNotice>
        {:else if section === "runtime"}
          <StudioPanel title="Agent loop" description="Bound model turns, delegation depth, continuation, and history behavior.">
            <div class="grid gap-4 md:grid-cols-2">
              <label class="grid gap-1"><span class="studio-label">Maximum Agentic iterations</span>
                <input class="studio-input" type="number" min="1" value={numberText(get("maxAgenticIterations"))}
                  oninput={event => setNumber("maxAgenticIterations", event.currentTarget.value)} />
              </label>
              <label class="grid gap-1"><span class="studio-label">Maximum subagent depth</span>
                <input class="studio-input" type="number" min="0" value={numberText(get("maxSubAgentDepth"))}
                  oninput={event => setNumber("maxSubAgentDepth", event.currentTarget.value)} />
              </label>
              <label class="grid gap-1"><span class="studio-label">Continuation extension</span>
                <input class="studio-input" type="number" min="0" value={numberText(get("continuationExtensionAmount"))}
                  oninput={event => setNumber("continuationExtensionAmount", event.currentTarget.value)} />
              </label>
              <label class="grid gap-1"><span class="studio-label">Parallel functions</span>
                <input class="studio-input" type="number" min="1" value={numberText(get("agenticLoop.maxParallelFunctions"))}
                  placeholder="Unlimited" oninput={event => setNumber("agenticLoop.maxParallelFunctions", event.currentTarget.value)} />
              </label>
              <label class="grid gap-1"><span class="studio-label">Maximum turn duration</span>
                <input class="studio-input font-mono" value={text(get("agenticLoop.maxTurnDuration"))} placeholder="00:05:00"
                  oninput={event => set("agenticLoop.maxTurnDuration", event.currentTarget.value)} />
              </label>
              <label class="grid gap-1"><span class="studio-label">Parent communication</span>
                <select class="studio-input" value={triState(get("enableParentCommunication"))}
                  onchange={event => set("enableParentCommunication", event.currentTarget.value === "inherit" ? undefined : event.currentTarget.value === "true")}>
                  <option value="inherit">Framework default</option><option value="true">Enabled</option><option value="false">Disabled</option>
                </select>
              </label>
            </div>
          </StudioPanel>
          <StudioPanel title="Behavior controls">
            <div class="grid gap-3 sm:grid-cols-2">
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={draft.allowUserQuestions === true}
                onchange={event => set("allowUserQuestions", event.currentTarget.checked)} />Expose AskUser</label>
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={draft.includeReasoningInModelHistory === true}
                onchange={event => set("includeReasoningInModelHistory", event.currentTarget.checked)} />Include prior reasoning in model history</label>
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={draft.coalesceDeltas === true}
                onchange={event => set("coalesceDeltas", event.currentTarget.checked)} />Coalesce streaming deltas</label>
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={get("agenticLoop.terminateOnUnknownCalls") === true}
                onchange={event => set("agenticLoop.terminateOnUnknownCalls", event.currentTarget.checked)} />Terminate on unknown function calls</label>
            </div>
          </StudioPanel>
          <StudioPanel title="Subagent fork projection" description="Controls how durable child threads follow a parent fork.">
            <div class="grid gap-4 md:grid-cols-2">
              <label class="grid gap-1"><span class="studio-label">Direct children</span>
                <select class="studio-input" value={numberText(get("defaultSubAgentForkOptions.policy")) || "0"}
                  onchange={event => setNumber("defaultSubAgentForkOptions.policy", event.currentTarget.value)}>
                  <option value="0">Detach</option><option value="1">Share</option><option value="2">Fork direct children</option>
                </select>
              </label>
              <label class="grid gap-1"><span class="studio-label">Descendants</span>
                <select class="studio-input" value={numberText(get("defaultSubAgentForkOptions.descendantPolicy")) || "0"}
                  onchange={event => setNumber("defaultSubAgentForkOptions.descendantPolicy", event.currentTarget.value)}>
                  <option value="0">Detach</option><option value="1">Share</option><option value="2">Fork direct children</option>
                </select>
              </label>
            </div>
          </StudioPanel>
        {:else if section === "capabilities"}
          <StudioPanel title="ToolHarnesses and middleware" description="Enter registered names in invocation order. Rich references remain available in the section JSON.">
            <div class="grid gap-4">
              <label class="grid gap-1"><span class="studio-label">ToolHarnesses</span>
                <input class="studio-input font-mono" value={listText(get("toolHarnesses"))} placeholder="support-harness, research-harness"
                  oninput={event => setList("toolHarnesses", event.currentTarget.value)} />
              </label>
              <label class="grid gap-1"><span class="studio-label">Middleware</span>
                <input class="studio-input font-mono" value={listText(get("middlewares"))} placeholder="LoggingMiddleware, RateLimitMiddleware"
                  oninput={event => setList("middlewares", event.currentTarget.value)} />
              </label>
            </div>
          </StudioPanel>
          <StudioPanel title="Tool visibility and collapsing">
            <div class="grid gap-3 sm:grid-cols-2">
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={get("collapsing.enabled") !== false}
                onchange={event => set("collapsing.enabled", event.currentTarget.checked)} />Collapse ToolHarness functions</label>
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={get("collapsing.collapseClientTools") === true}
                onchange={event => set("collapsing.collapseClientTools", event.currentTarget.checked)} />Collapse client tools</label>
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={get("collapsing.enableErrorRecovery") !== false}
                onchange={event => set("collapsing.enableErrorRecovery", event.currentTarget.checked)} />Recover invalid container calls</label>
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={get("collapsing.hideToolHarnessInteractionsWithinTurn") !== false}
                onchange={event => set("collapsing.hideToolHarnessInteractionsWithinTurn", event.currentTarget.checked)} />Hide container interactions within the turn</label>
              <label class="grid gap-1"><span class="studio-label">Function names in generated descriptions</span>
                <input class="studio-input" type="number" min="0" value={numberText(get("collapsing.maxFunctionNamesInDescription"))}
                  oninput={event => setNumber("collapsing.maxFunctionNamesInDescription", event.currentTarget.value)} />
              </label>
              <label class="grid gap-1"><span class="studio-label">Tool selection mode</span>
                <select class="studio-input" value={text(get("toolSelection.toolMode")) || "Auto"}
                  onchange={event => set("toolSelection.toolMode", event.currentTarget.value)}>
                  {#each ["Auto", "None", "RequireAny", "RequireSpecific"] as option}<option value={option}>{option}</option>{/each}
                </select>
              </label>
              {#if get("toolSelection.toolMode") === "RequireSpecific"}
                <label class="grid gap-1 sm:col-span-2"><span class="studio-label">Required function</span>
                  <input class="studio-input font-mono" value={text(get("toolSelection.requiredFunctionName"))}
                    oninput={event => set("toolSelection.requiredFunctionName", event.currentTarget.value)} />
                </label>
              {/if}
            </div>
          </StudioPanel>
          <StudioPanel title="Tool output and skills">
            <div class="grid gap-3 sm:grid-cols-2">
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={get("toolOutputs.enabled") !== false}
                onchange={event => set("toolOutputs.enabled", event.currentTarget.checked)} />Enable tool-result offloading</label>
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={get("toolOutputs.contentRetrievalEnabled") !== false}
                onchange={event => set("toolOutputs.contentRetrievalEnabled", event.currentTarget.checked)} />Allow stored-content retrieval</label>
              <label class="grid gap-1"><span class="studio-label">Inline result bytes</span>
                <input class="studio-input" type="number" min="1" value={numberText(get("toolOutputs.maxInlineResultBytes"))}
                  oninput={event => setNumber("toolOutputs.maxInlineResultBytes", event.currentTarget.value)} />
              </label>
              <label class="grid gap-1"><span class="studio-label">Skill instruction delivery</span>
                <select class="studio-input" value={numberText(get("skills.instructionDelivery")) || "0"}
                  onchange={event => setNumber("skills.instructionDelivery", event.currentTarget.value)}>
                  <option value="0">Tool result</option><option value="1">Tool result + system reinforcement</option>
                </select>
              </label>
            </div>
          </StudioPanel>
        {:else if section === "reliability"}
          <StudioPanel title="Validation and errors">
            <div class="grid gap-3 sm:grid-cols-2">
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={get("validation.enableAsyncValidation") === true}
                onchange={event => set("validation.enableAsyncValidation", event.currentTarget.checked)} />Run async provider validation</label>
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={get("validation.failOnValidationError") === true}
                onchange={event => set("validation.failOnValidationError", event.currentTarget.checked)} />Fail construction on validation errors</label>
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={get("errorHandling.normalizeErrors") !== false}
                onchange={event => set("errorHandling.normalizeErrors", event.currentTarget.checked)} />Normalize provider errors</label>
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={get("errorHandling.includeDetailedErrorsInChat") !== false}
                onchange={event => set("errorHandling.includeDetailedErrorsInChat", event.currentTarget.checked)} />Include detailed tool errors in chat</label>
            </div>
          </StudioPanel>
          <StudioPanel title="Retries and loop protection">
            <div class="grid gap-4 md:grid-cols-2">
              <label class="grid gap-1"><span class="studio-label">Model retry count</span>
                <input class="studio-input" type="number" min="0" value={numberText(get("resilience.retry.modelRetries.maxRetries"))}
                  oninput={event => setNumber("resilience.retry.modelRetries.maxRetries", event.currentTarget.value)} /></label>
              <label class="grid gap-1"><span class="studio-label">Function retry count</span>
                <input class="studio-input" type="number" min="0" value={numberText(get("resilience.retry.functionRetries.maxRetries"))}
                  oninput={event => setNumber("resilience.retry.functionRetries.maxRetries", event.currentTarget.value)} /></label>
              <label class="grid gap-1"><span class="studio-label">Circuit breaker calls</span>
                <input class="studio-input" type="number" min="1" value={numberText(get("resilience.circuitBreaker.maxConsecutiveCalls"))}
                  oninput={event => setNumber("resilience.circuitBreaker.maxConsecutiveCalls", event.currentTarget.value)} /></label>
              <label class="grid gap-1"><span class="studio-label">Consecutive error limit</span>
                <input class="studio-input" type="number" min="1" value={numberText(get("resilience.consecutiveErrors.maxConsecutiveErrors"))}
                  oninput={event => setNumber("resilience.consecutiveErrors.maxConsecutiveErrors", event.currentTarget.value)} /></label>
              <label class="grid gap-1"><span class="studio-label">Function timeout</span>
                <input class="studio-input font-mono" value={text(get("resilience.functionTimeout"))} placeholder="00:00:30"
                  oninput={event => set("resilience.functionTimeout", event.currentTarget.value)} /></label>
              <label class="grid gap-1"><span class="studio-label">Maximum total errors</span>
                <input class="studio-input" type="number" min="1" value={numberText(get("resilience.totalErrors.maxTotalErrors"))}
                  oninput={event => setNumber("resilience.totalErrors.maxTotalErrors", event.currentTarget.value)} /></label>
            </div>
          </StudioPanel>
          <StudioPanel title="Goals and background responses">
            <div class="grid gap-3 sm:grid-cols-2">
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={get("goals.enabled") === true}
                onchange={event => set("goals.enabled", event.currentTarget.checked)} />Enable persistent Goals</label>
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={get("goals.allowModelCreatedGoals") !== false}
                onchange={event => set("goals.allowModelCreatedGoals", event.currentTarget.checked)} />Allow model-created Goals</label>
              <label class="grid gap-1"><span class="studio-label">Goal objective limit</span>
                <input class="studio-input" type="number" min="1" value={numberText(get("goals.maximumObjectiveLength"))}
                  oninput={event => setNumber("goals.maximumObjectiveLength", event.currentTarget.value)} /></label>
              <label class="flex items-center gap-2 text-body-sm"><input type="checkbox" checked={get("backgroundResponses.defaultAllow") === true}
                onchange={event => set("backgroundResponses.defaultAllow", event.currentTarget.checked)} />Allow background responses by default</label>
            </div>
          </StudioPanel>
          <StudioPanel title="Retention and shutdown">
            <div class="grid gap-4 md:grid-cols-2">
              <label class="grid gap-1"><span class="studio-label">Terminal retention</span>
                <input class="studio-input font-mono" value={text(get("operationRetention.terminalRetention")) || "30.00:00:00"}
                  oninput={event => set("operationRetention.terminalRetention", event.currentTarget.value)} /></label>
              <label class="grid gap-1"><span class="studio-label">Provider deduplication retention</span>
                <input class="studio-input font-mono" value={text(get("operationRetention.providerDeduplicationRetention")) || "90.00:00:00"}
                  oninput={event => set("operationRetention.providerDeduplicationRetention", event.currentTarget.value)} /></label>
              <label class="grid gap-1"><span class="studio-label">Graceful drain timeout</span>
                <input class="studio-input font-mono" value={text(get("shutdown.gracefulDrainTimeout")) || "00:00:30"}
                  oninput={event => set("shutdown.gracefulDrainTimeout", event.currentTarget.value)} /></label>
              <label class="grid gap-1"><span class="studio-label">Cancellation drain timeout</span>
                <input class="studio-input font-mono" value={text(get("shutdown.cancellationDrainTimeout")) || "00:00:10"}
                  oninput={event => set("shutdown.cancellationDrainTimeout", event.currentTarget.value)} /></label>
            </div>
          </StudioPanel>
        {:else}
          <StudioNotice tone="info" title="Complete configuration coverage">
            <p>Every remaining serializable AgentConfig section is exposed with typed controls, repeatable collections, and bounded JSON only for provider-owned or polymorphic leaves.</p>
          </StudioNotice>
          <StructuredConfigurationEditor config={draft} onchange={(path, value) => set(path, value)} />
        {/if}
      </div>

      <aside class="grid content-start gap-4 xl:sticky xl:top-4 xl:max-h-[calc(100vh-2rem)]">
        <StudioPanel title="Materialized definition" description="Generated from the visual controls. This is the exact object submitted for review.">
          <div class="flex flex-wrap items-center justify-between gap-2">
            <div class="flex gap-2">
              <StudioButton size="small" variant={materialization === "json" ? "primary" : "secondary"}
                type="button" onclick={() => materialization = "json"}>JSON</StudioButton>
              <StudioButton size="small" variant={materialization === "yaml" ? "primary" : "secondary"}
                type="button" onclick={() => materialization = "yaml"}>YAML</StudioButton>
            </div>
            <StudioStatusBadge>{Object.keys(materializedConfig as Record<string, unknown>).length} top-level sections</StudioStatusBadge>
          </div>
          <pre class="mt-3 max-h-[38rem] overflow-auto whitespace-pre-wrap rounded-studio border border-studio-line bg-studio-panel-raised p-3 font-mono text-xs studio-text-safe">{materialization === "json" ? configJson : configYaml}</pre>
        </StudioPanel>

        {#if commandKind !== "closed"}
          <StudioPanel title="Reviewed command" description="Creation is bound to the application authority and the canonical checksum below.">
            <div class="flex flex-wrap gap-2">
              <StudioStatusBadge tone={commandKind === "review" ? "warning" : commandKind === "confirmed" || commandKind === "duplicate" ? "success" : commandKind === "failed" ? "danger" : "info"}>{commandKind}</StudioStatusBadge>
              <StudioStatusBadge>agent.create</StudioStatusBadge>
            </div>
            <dl class="grid gap-2 text-body-sm">
              <div><dt class="studio-label">Agent</dt><dd>{preview?.name || text(draft.name)} · <span class="font-mono">{preview?.agentId || agentId}</span></dd></div>
              <div><dt class="studio-label">Config checksum</dt><dd class="break-all font-mono text-xs">{preview?.configChecksum || "Unavailable"}</dd></div>
              <div><dt class="studio-label">Preview expires</dt><dd>{preview?.expiresAtUtc || "Unavailable"}</dd></div>
            </dl>
            {#if commandKind === "review"}
              <label class="flex items-start gap-2 rounded-studio border border-studio-line bg-studio-panel-soft p-3 text-body-sm">
                <input class="mt-1" type="checkbox" checked={accepted} onchange={event => acknowledge(event.currentTarget.checked)} />
                <span><strong>I reviewed the Agent ID and materialized configuration.</strong><span class="mt-1 block text-studio-muted">The host revalidates the complete definition and ID availability before creation.</span></span>
              </label>
              <div class="flex flex-wrap gap-2">
                <StudioButton variant="primary" type="button" loading={busy} disabled={!accepted}
                  onclick={() => void createAgent()}>Create Agent</StudioButton>
                <StudioButton type="button" onclick={() => resetReview()}>Discard review</StudioButton>
              </div>
            {/if}
          </StudioPanel>
        {/if}
      </aside>
    </div>
  </div>
</main>
