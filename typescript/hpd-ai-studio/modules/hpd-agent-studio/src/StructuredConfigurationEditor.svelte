<script lang="ts">
  import { StudioButton, StudioStatusBadge } from '@hpd-research/hpd-studio-design';
  import StructuredField from './StructuredField.svelte';

  interface Option {
    readonly label: string;
    readonly value: string | number;
  }

  interface FieldSpec {
    readonly path: string;
    readonly label: string;
    readonly type?: "text" | "number" | "boolean" | "select" | "list" | "multiline" | "json";
    readonly options?: readonly Option[];
    readonly min?: number;
    readonly max?: number;
    readonly step?: number;
    readonly placeholder?: string;
    readonly hint?: string;
  }

  interface ListSpec {
    readonly path: string;
    readonly label: string;
    readonly description: string;
    readonly itemLabel: string;
    readonly create: () => Record<string, unknown>;
    readonly fields: readonly FieldSpec[];
  }

  interface GroupSpec {
    readonly key: string;
    readonly title: string;
    readonly description: string;
    readonly fields?: readonly FieldSpec[];
    readonly lists?: readonly ListSpec[];
    readonly special?: "clients" | "compaction";
  }

  interface Props {
    config: Record<string, any>;
    onchange: (path: string, value: unknown) => void;
  }

  let { config, onchange }: Props = $props();
  let clientFamily: string = $state("chat");
  const clientFamilies: readonly Option[] = Object.freeze([
    { value: "chat", label: "Chat" },
    { value: "textToSpeech", label: "Text to speech" },
    { value: "speechToText", label: "Speech to text" },
    { value: "realtime", label: "Realtime" },
    { value: "imageGeneration", label: "Image generation" },
    { value: "embeddings", label: "Embeddings" },
    { value: "hostedFiles", label: "Hosted files" },
    { value: "voiceActivity", label: "Voice activity detection" },
    { value: "endOfTurn", label: "End-of-turn detection" },
  ]);
  const booleanOptions: readonly Option[] = Object.freeze([
    { value: 0, label: "Detach" },
    { value: 1, label: "Share" },
    { value: 2, label: "Fork direct children" },
  ]);
  const strategyOptions: readonly Option[] = Object.freeze([
    { value: 0, label: "Block" },
    { value: 1, label: "Redact" },
    { value: 2, label: "Mask" },
    { value: 3, label: "Hash" },
    { value: 4, label: "Allow" },
  ]);
  const groups: readonly GroupSpec[] = Object.freeze([
    {
      key: "clients",
      title: "Provider client families",
      description: "Select a family, then configure its provider reference and sampling defaults. Provider-specific payloads remain bounded JSON.",
      special: "clients",
    },
    {
      key: "defaultSubAgentForkOptions",
      title: "Subagent fork projection",
      description: "Durable child-thread topology inherited by parent thread forks.",
      fields: [
        { path: "policy", label: "Direct children", type: "select", options: booleanOptions },
        { path: "descendantPolicy", label: "Descendants", type: "select", options: booleanOptions },
      ],
    },
    {
      key: "toolHarnesses",
      title: "Rich ToolHarness references",
      description: "Owner identity, selected functions, settings payload, and metadata for each capability composition.",
      lists: [{
        path: "",
        label: "ToolHarnesses",
        description: "Use rich references for function filtering or ToolHarness-specific configuration.",
        itemLabel: "ToolHarness",
        create: () => ({ name: "", functions: [], config: null, metadata: null }),
        fields: [
          { path: "name", label: "Owner identity", placeholder: "ToolHarness owner name" },
          { path: "functions", label: "Selected functions", type: "list", placeholder: "ReadFile, WriteFile" },
          { path: "config", label: "Settings payload", type: "json", hint: "Must match the generated ToolHarness settings contract." },
          { path: "metadata", label: "Metadata payload", type: "json", hint: "Must match the generated metadata contract." },
        ],
      }],
    },
    {
      key: "middlewares",
      title: "Rich middleware references",
      description: "Middleware owner identity and its typed constructor configuration.",
      lists: [{
        path: "",
        label: "Middleware",
        description: "Configured in declaration order.",
        itemLabel: "Middleware",
        create: () => ({ name: "", config: null }),
        fields: [
          { path: "name", label: "Owner identity", placeholder: "Middleware owner name" },
          { path: "config", label: "Configuration payload", type: "json" },
        ],
      }],
    },
    {
      key: "providerProfiles",
      title: "Provider backend profiles",
      description: "Explicit provider/backend identities and their family baselines.",
      lists: [{
        path: "",
        label: "Profiles",
        description: "Each profile binds one provider package and backend to a client-family baseline.",
        itemLabel: "Provider profile",
        create: () => ({ providerKey: "", backendKey: "", clients: { transport: 0 } }),
        fields: [
          { path: "providerKey", label: "Provider key", placeholder: "provider.package.identity" },
          { path: "backendKey", label: "Backend key", placeholder: "backend.identity" },
          { path: "clients", label: "Client baselines", type: "json",
            hint: "Use the Clients section shape for provider/backend-specific family baselines." },
        ],
      }],
    },
    {
      key: "providerDefaults",
      title: "Provider family defaults",
      description: "Default provider and backend for each client family.",
      lists: [{
        path: "",
        label: "Defaults",
        description: "Select the family and the provider/backend that should resolve by default.",
        itemLabel: "Family default",
        create: () => ({ family: 0, providerKey: "", backendKey: "" }),
        fields: [
          { path: "family", label: "Client family", type: "select", options: clientFamilies.map((item, index) => ({
            value: index, label: item.label,
          })) },
          { path: "providerKey", label: "Provider key", placeholder: "provider.package.identity" },
          { path: "backendKey", label: "Backend key", placeholder: "backend.identity" },
        ],
      }],
    },
    {
      key: "validation",
      title: "Validation",
      description: "Construction-time provider validation behavior.",
      fields: [
        { path: "enableAsyncValidation", label: "Async provider validation", type: "boolean" },
        { path: "timeoutMs", label: "Validation timeout (ms)", type: "number", min: 1 },
        { path: "failOnValidationError", label: "Fail construction on error", type: "boolean" },
      ],
    },
    {
      key: "errorHandling",
      title: "Error handling",
      description: "Provider error normalization and model-visible error detail.",
      fields: [
        { path: "normalizeErrors", label: "Normalize provider errors", type: "boolean" },
        { path: "includeProviderDetails", label: "Include provider details", type: "boolean" },
        { path: "includeDetailedErrorsInChat", label: "Include detailed tool errors", type: "boolean" },
      ],
    },
    {
      key: "resilience",
      title: "Resilience policies",
      description: "Retry, circuit-breaker, error-budget, and function timeout policy.",
      fields: [
        { path: "retry.modelRetries.maxRetries", label: "Model retries", type: "number", min: 0 },
        { path: "retry.modelRetries.retryDelay", label: "Model retry delay", placeholder: "00:00:01" },
        { path: "retry.modelRetries.maxRetryDelay", label: "Maximum model retry delay", placeholder: "00:00:30" },
        { path: "retry.modelRetries.backoffMultiplier", label: "Backoff multiplier", type: "number", min: 1, step: 0.1 },
        { path: "retry.modelRetries.autoRefreshTokensOn401", label: "Refresh token on 401", type: "boolean" },
        { path: "retry.functionRetries.maxRetries", label: "Function retries", type: "number", min: 0 },
        { path: "retry.functionRetries.retryDelay", label: "Function retry delay", placeholder: "00:00:01" },
        { path: "retry.functionRetries.maxRetryDelay", label: "Maximum function retry delay", placeholder: "00:00:30" },
        { path: "circuitBreaker.maxConsecutiveCalls", label: "Circuit breaker call threshold", type: "number", min: 1 },
        { path: "consecutiveErrors.maxConsecutiveErrors", label: "Consecutive error limit", type: "number", min: 1 },
        { path: "totalErrors.maxTotalErrors", label: "Total error limit", type: "number", min: 1 },
        { path: "functionTimeout", label: "Function timeout", placeholder: "00:00:30" },
      ],
    },
    {
      key: "privacy",
      title: "Privacy and PII",
      description: "Deterministic redaction at model and tool boundaries.",
      fields: [
        { path: "pii.emailStrategy", label: "Email strategy", type: "select", options: strategyOptions },
        { path: "pii.creditCardStrategy", label: "Credit-card strategy", type: "select", options: strategyOptions },
        { path: "pii.ssnStrategy", label: "SSN strategy", type: "select", options: strategyOptions },
        { path: "pii.phoneStrategy", label: "Phone strategy", type: "select", options: strategyOptions },
        { path: "pii.ipAddressStrategy", label: "IP-address strategy", type: "select", options: strategyOptions },
        { path: "pii.applyToInput", label: "Apply to input", type: "boolean" },
        { path: "pii.applyToOutput", label: "Apply to output", type: "boolean" },
        { path: "pii.applyToToolResults", label: "Apply to tool results", type: "boolean" },
        { path: "pii.externalDetector", label: "External detector key", placeholder: "host-registered detector" },
      ],
      lists: [{
        path: "pii.customDetectors",
        label: "Declarative detectors",
        description: "Bounded regular-expression detectors with their replacement behavior.",
        itemLabel: "Custom detector",
        create: () => ({ name: "", pattern: "", strategy: 1, replacement: null }),
        fields: [
          { path: "name", label: "Name" },
          { path: "pattern", label: "Regular expression", placeholder: "\\b[A-Z]{2}-\\d{4}\\b" },
          { path: "strategy", label: "Strategy", type: "select", options: strategyOptions },
          { path: "replacement", label: "Replacement", placeholder: "[REDACTED]" },
        ],
      }],
    },
    {
      key: "compaction",
      title: "Conversation compaction",
      description: "Automatic trigger, preservation, strategy, and commit behavior.",
      special: "compaction",
    },
    {
      key: "goals",
      title: "Persistent Goals",
      description: "Goal capability limits and runtime policy selection.",
      fields: [
        { path: "enabled", label: "Enable Goals", type: "boolean" },
        { path: "allowModelCreatedGoals", label: "Allow model-created Goals", type: "boolean" },
        { path: "maximumObjectiveLength", label: "Maximum objective length", type: "number", min: 1 },
        { path: "requiredConsecutiveBlockerExecutions", label: "Blocker persistence count", type: "number", min: 1 },
        { path: "maximumConsecutiveNoProgressExecutions", label: "No-progress execution limit", type: "number", min: 1 },
        { path: "policies.completion", label: "Completion policy", placeholder: "default" },
        { path: "policies.blocker", label: "Blocker policy", placeholder: "default" },
        { path: "policies.continuation", label: "Continuation policy", placeholder: "default" },
        { path: "policies.accounting", label: "Accounting policy", placeholder: "default" },
        { path: "policies.fork", label: "Fork policy", placeholder: "default" },
      ],
    },
    {
      key: "agenticLoop",
      title: "Agentic loop safety",
      description: "Per-turn duration, parallel function admission, and unknown-call behavior.",
      fields: [
        { path: "maxTurnDuration", label: "Maximum turn duration", placeholder: "00:05:00" },
        { path: "maxParallelFunctions", label: "Maximum parallel functions", type: "number", min: 1 },
        { path: "terminateOnUnknownCalls", label: "Terminate on unknown calls", type: "boolean" },
      ],
    },
    {
      key: "messages",
      title: "System message templates",
      description: "Localized or branded termination messages used by the runtime.",
      fields: [
        { path: "maxIterationsReached", label: "Maximum iterations message", type: "multiline" },
        { path: "circuitBreakerTriggered", label: "Circuit-breaker message", type: "multiline" },
        { path: "maxConsecutiveErrors", label: "Consecutive errors message", type: "multiline" },
        { path: "permissionDeniedDefault", label: "Permission denied message", type: "multiline" },
      ],
    },
    {
      key: "toolSelection",
      title: "Tool selection",
      description: "Controls whether the model may decide, must call a function, or receives no tools.",
      fields: [
        { path: "toolMode", label: "Tool mode", type: "select", options: [
          { value: "Auto", label: "Auto" }, { value: "None", label: "None" },
          { value: "RequireAny", label: "Require any function" }, { value: "RequireSpecific", label: "Require specific function" },
        ] },
        { path: "requiredFunctionName", label: "Required function name", placeholder: "function-name" },
      ],
    },
    {
      key: "collapsing",
      title: "ToolHarness collapsing",
      description: "Container visibility, recovery, and prompt-injection behavior.",
      fields: [
        { path: "enabled", label: "Enable collapsing", type: "boolean" },
        { path: "collapseClientTools", label: "Collapse client tools", type: "boolean" },
        { path: "maxFunctionNamesInDescription", label: "Function names in description", type: "number", min: 0 },
        { path: "persistSystemPromptInjections", label: "Persist prompt injections", type: "boolean" },
        { path: "enableErrorRecovery", label: "Enable error recovery", type: "boolean" },
        { path: "recoveryHistoryMode", label: "Recovery history", type: "select", options: [
          { value: 0, label: "Preserve" }, { value: 1, label: "Rewrite" },
        ] },
        { path: "hideToolHarnessInteractionsWithinTurn", label: "Hide interactions within turn", type: "boolean" },
        { path: "clientToolsInstructions", label: "Client tools instructions", type: "multiline" },
      ],
    },
    {
      key: "skills",
      title: "Skill runtime",
      description: "Instruction delivery for activated skills.",
      fields: [{
        path: "instructionDelivery", label: "Instruction delivery", type: "select", options: [
          { value: 0, label: "Tool result" }, { value: 1, label: "Tool result + system reinforcement" },
        ],
      }],
    },
    {
      key: "toolOutputs",
      title: "Tool output projection",
      description: "Native [Offload] projection, retrieval, and bounded content limits.",
      fields: [
        { path: "enabled", label: "Enable offload projection", type: "boolean" },
        { path: "maxInlineResultBytes", label: "Inline result byte budget", type: "number", min: 1 },
        { path: "failureMode", label: "Failure mode", type: "select", options: [
          { value: 0, label: "Fail closed" }, { value: 1, label: "Preserve inline" },
        ] },
        { path: "contentRetrievalEnabled", label: "Enable retrieval", type: "boolean" },
        { path: "retrievalLimits.maxListResultsPerCall", label: "List results per call", type: "number", min: 1 },
        { path: "retrievalLimits.maxReadBytesPerCall", label: "Read bytes per call", type: "number", min: 1 },
        { path: "retrievalLimits.maxReadBytesPerTurn", label: "Read bytes per turn", type: "number", min: 1 },
        { path: "retrievalLimits.maxReadCallsPerTurn", label: "Read calls per turn", type: "number", min: 1 },
        { path: "retrievalLimits.maxListCallsPerTurn", label: "List calls per turn", type: "number", min: 1 },
      ],
    },
    {
      key: "observability",
      title: "Observability runtime",
      description: "Sampling, observer concurrency, and logging detail.",
      fields: [
        { path: "enableSampling", label: "Enable high-volume sampling", type: "boolean" },
        { path: "textDeltaSamplingRate", label: "Text-delta sampling", type: "number", min: 0, max: 1, step: 0.05 },
        { path: "reasoningDeltaSamplingRate", label: "Reasoning-delta sampling", type: "number", min: 0, max: 1, step: 0.05 },
        { path: "emitObservabilityEvents", label: "Emit observability events", type: "boolean" },
        { path: "enableSensitiveLogging", label: "Enable sensitive logging", type: "boolean" },
        { path: "maxConcurrentObservers", label: "Concurrent observers", type: "number", min: 1 },
        { path: "maxConsecutiveFailures", label: "Observer failure threshold", type: "number", min: 1 },
        { path: "successesToResetCircuitBreaker", label: "Successes to reset", type: "number", min: 1 },
        { path: "logging.logMessageTurn", label: "Log message turns", type: "boolean" },
        { path: "logging.logIteration", label: "Log iterations", type: "boolean" },
        { path: "logging.logFunction", label: "Log functions", type: "boolean" },
        { path: "logging.maxStringLength", label: "Maximum logged string", type: "number", min: 0 },
        { path: "logging.logPrefix", label: "Log prefix" },
      ],
    },
    {
      key: "backgroundResponses",
      title: "Background responses",
      description: "Provider background operation defaults and polling limits.",
      fields: [
        { path: "defaultAllow", label: "Allow by default", type: "boolean" },
        { path: "defaultPollingInterval", label: "Polling interval", placeholder: "00:00:02" },
        { path: "defaultTimeout", label: "Default timeout", placeholder: "00:10:00" },
        { path: "autoPollToCompletion", label: "Auto-poll to completion", type: "boolean" },
        { path: "maxPollAttempts", label: "Maximum poll attempts", type: "number", min: 1 },
      ],
    },
    {
      key: "audio",
      title: "Audio",
      description: "Provider-neutral audio modes, pacing, artifact capture, and playback.",
      fields: [
        { path: "enabled", label: "Enable audio", type: "boolean" },
        { path: "inputMode", label: "Input mode", type: "select", options: [
          { value: 0, label: "Auto" }, { value: 1, label: "None" }, { value: 2, label: "Batch speech-to-text" },
          { value: 3, label: "Provider realtime" }, { value: 4, label: "Reference only" },
          { value: 5, label: "Reject" }, { value: 6, label: "Streaming speech-to-text" },
        ] },
        { path: "outputMode", label: "Output mode", type: "select", options: [
          { value: 0, label: "Auto" }, { value: 1, label: "None" }, { value: 2, label: "Text only" },
          { value: 3, label: "Text to speech" }, { value: 4, label: "Provider realtime audio" },
        ] },
        { path: "enablePlayback", label: "Enable playback", type: "boolean" },
        { path: "transport.componentInstance", label: "Transport component", placeholder: "registered component instance" },
        { path: "transport.endpoint", label: "Transport endpoint", placeholder: "provider endpoint" },
        { path: "transport.version", label: "Transport schema version", type: "number", min: 0 },
        { path: "pacing.mode", label: "Pacing mode", type: "select", options: [
          { value: 0, label: "Sentence" }, { value: 1, label: "Phrase" },
          { value: 2, label: "Token batch" }, { value: 3, label: "Manual" },
        ] },
        { path: "policy", label: "Audio policy set", type: "json" },
        { path: "transport.providerConfig", label: "Provider transport config", type: "json" },
      ],
    },
    {
      key: "shutdown",
      title: "Shutdown",
      description: "Bounded drain, remote cancellation, and leaked lease behavior.",
      fields: [
        { path: "gracefulDrainTimeout", label: "Graceful drain timeout", placeholder: "00:00:30" },
        { path: "cancellationDrainTimeout", label: "Cancellation drain timeout", placeholder: "00:00:10" },
        { path: "remoteOperations", label: "Remote operation policy", type: "select", options: [
          { value: 0, label: "Detach observation" }, { value: 1, label: "Request cancellation" },
        ] },
        { path: "leaseLeaks", label: "Lease leak policy", type: "select", options: [
          { value: 0, label: "Report and abandon" }, { value: 1, label: "Report and force dispose" },
        ] },
      ],
    },
    {
      key: "operationRetention",
      title: "Operation retention",
      description: "Terminal snapshots and provider replay-deduplication retention.",
      fields: [
        { path: "terminalRetention", label: "Terminal retention", placeholder: "30.00:00:00" },
        { path: "providerDeduplicationRetention", label: "Provider deduplication retention", placeholder: "90.00:00:00" },
        { path: "maximumTerminalOperationsPerThread", label: "Maximum operations per thread", type: "number", min: 1 },
      ],
    },
    {
      key: "mcp",
      title: "MCP sources",
      description: "Inline MCP server declarations and external manifests.",
      lists: [
        {
          path: "manifests",
          label: "Manifest references",
          description: "External JSON MCP manifests resolved by the host.",
          itemLabel: "Manifest",
          create: () => ({ path: "", sourceName: null }),
          fields: [
            { path: "path", label: "Manifest path", placeholder: "./mcp.json" },
            { path: "sourceName", label: "Source name", placeholder: "optional diagnostic alias" },
          ],
        },
        {
          path: "servers",
          label: "Inline servers",
          description: "Portable stdio or Streamable HTTP server registrations.",
          itemLabel: "MCP server",
          create: () => ({ name: "", transport: "stdio", enabled: true, arguments: [] }),
          fields: [
            { path: "name", label: "Name", placeholder: "filesystem" },
            { path: "transport", label: "Transport", type: "select", options: [
              { value: "stdio", label: "stdio" }, { value: "http", label: "Streamable HTTP" },
            ] },
            { path: "enabled", label: "Enabled", type: "boolean" },
            { path: "command", label: "stdio command", placeholder: "npx" },
            { path: "arguments", label: "stdio arguments", type: "list", placeholder: "-y, package-name" },
            { path: "workingDirectory", label: "Working directory" },
            { path: "endpoint", label: "HTTP endpoint", placeholder: "https://mcp.example" },
            { path: "description", label: "Description", type: "multiline" },
            { path: "enableCollapsing", label: "Collapse functions", type: "boolean" },
            { path: "enableResources", label: "Expose resources", type: "boolean" },
            { path: "enablePrompts", label: "Expose prompts", type: "boolean" },
            { path: "exactVersion", label: "Exact protocol version" },
            { path: "headers", label: "HTTP headers", type: "json", hint: "Non-secret additive headers only." },
            { path: "environment", label: "Environment", type: "json" },
            { path: "permission", label: "Permission policy", type: "json" },
            { path: "oauth", label: "OAuth policy", type: "json" },
          ],
        },
      ],
    },
  ]);

  function rootValue(key: string): unknown {
    return config[key];
  }

  function valueAt(key: string, path: string): unknown {
    if (!path) return rootValue(key);
    return path.split(".").reduce<unknown>((current, part) =>
      current !== null && typeof current === "object"
        ? (current as Record<string, unknown>)[part]
        : undefined, rootValue(key));
  }

  function change(key: string, path: string, value: unknown): void {
    onchange(path ? `${key}.${path}` : key, value);
  }

  function items(key: string, path: string): Record<string, unknown>[] {
    const candidate = path ? valueAt(key, path) : rootValue(key);
    return Array.isArray(candidate)
      ? candidate.flatMap(item => item && typeof item === "object" ? [item as Record<string, unknown>] : [])
      : [];
  }

  function addItem(key: string, list: ListSpec): void {
    const prior = valueAt(key, list.path);
    change(key, list.path, [...(Array.isArray(prior) ? prior : []), list.create()]);
  }

  function removeItem(key: string, list: ListSpec, index: number): void {
    change(key, list.path, items(key, list.path).filter((_, current) => current !== index));
  }

  function itemChange(key: string, list: ListSpec, index: number, field: string, value: unknown): void {
    const next = items(key, list.path).map(item => ({ ...item }));
    const parts = field.split(".");
    let target = next[index];
    for (const part of parts.slice(0, -1)) {
      const child = target[part];
      target[part] = child && typeof child === "object" && !Array.isArray(child) ? { ...(child as object) } : {};
      target = target[part] as Record<string, unknown>;
    }
    const final = parts.at(-1)!;
    if (value === undefined || value === "") delete target[final];
    else target[final] = value;
    change(key, list.path, next);
  }

  function configured(key: string): boolean {
    const value = rootValue(key);
    return value !== undefined && value !== null &&
      (typeof value !== "object" || Object.keys(value as object).length > 0);
  }

  function ensureCompaction(): void {
    change("compaction", "", {
      automatic: {
        trigger: { "$type": "turnCount", turns: 20 },
        compaction: {
          point: { "$type": "currentHead" },
          preservation: { "$type": "none" },
          strategy: { "$type": "removal" },
          commitMode: 0,
        },
        continuation: 0,
      },
    });
  }

  function clientChange(field: string, value: unknown): void {
    change("clients", `${clientFamily}.${field}`, value);
  }

  function itemValue(item: Record<string, unknown>, path: string): unknown {
    return path.split(".").reduce<unknown>((current, part) =>
      current !== null && typeof current === "object"
        ? (current as Record<string, unknown>)[part]
        : undefined, item);
  }

  function textItem(item: Record<string, unknown>, list: ListSpec): string {
    const primary = item.name ?? item.providerKey ?? item.path ??
      list.fields.map(field => itemValue(item, field.path)).find(value => typeof value === "string");
    return typeof primary === "string" && primary.length > 0 ? primary : list.itemLabel;
  }
</script>

<div class="grid gap-3">
  {#each groups as group (group.key)}
    <details class="rounded-studio border border-studio-line bg-studio-panel p-4">
      <summary class="flex cursor-pointer flex-wrap items-center justify-between gap-2">
        <span><strong>{group.title}</strong><span class="ml-2 font-mono text-meta font-normal text-studio-muted">{group.key}</span></span>
        <StudioStatusBadge tone={configured(group.key) ? "info" : "neutral"}>{configured(group.key) ? "Configured" : "Uses defaults"}</StudioStatusBadge>
      </summary>
      <p class="mt-2 text-body-sm text-studio-muted">{group.description}</p>

      {#if group.special === "clients"}
        <div class="mt-4 grid gap-4">
          <StructuredField label="Model transport" type="select" value={valueAt("clients", "transport")}
            options={[{ value: 0, label: "Auto" }, { value: 1, label: "Chat" }, { value: 2, label: "Realtime" }]}
            onchange={value => change("clients", "transport", value)} />
          <StructuredField label="Client family" type="select" value={clientFamily} options={clientFamilies}
            hint="Choose a family here; the same provider reference and sampling controls are reused for every family."
            onchange={value => clientFamily = String(value)} />
          <div class="grid gap-4 md:grid-cols-2">
            <StructuredField label="Provider key" value={valueAt("clients", `${clientFamily}.provider.key`)}
              placeholder="provider.package.identity" onchange={value => clientChange("provider.key", value)} />
            <StructuredField label="Provider backend" value={valueAt("clients", `${clientFamily}.provider.backend`)}
              placeholder="backend.identity" onchange={value => clientChange("provider.backend", value)} />
            <StructuredField label="Model name" value={valueAt("clients", `${clientFamily}.modelName`)}
              placeholder="model-id" onchange={value => clientChange("modelName", value)} />
            <StructuredField label="Endpoint override" value={valueAt("clients", `${clientFamily}.endpoint`)}
              placeholder="optional endpoint" onchange={value => clientChange("endpoint", value)} />
            <StructuredField label="Temperature" type="number" min={0} max={2} step={0.1}
              value={valueAt("clients", `${clientFamily}.temperature`)} onchange={value => clientChange("temperature", value)} />
            <StructuredField label="Top P" type="number" min={0} max={1} step={0.05}
              value={valueAt("clients", `${clientFamily}.topP`)} onchange={value => clientChange("topP", value)} />
            <StructuredField label="Maximum output tokens" type="number" min={1}
              value={valueAt("clients", `${clientFamily}.maxOutputTokens`)} onchange={value => clientChange("maxOutputTokens", value)} />
            <StructuredField label="Custom headers" type="json"
              value={valueAt("clients", `${clientFamily}.customHeaders`)}
              hint="Authentication headers are rejected."
              onchange={value => clientChange("customHeaders", value)} />
          </div>
        </div>
      {:else if group.special === "compaction"}
        <div class="mt-4 grid gap-4">
          {#if valueAt("compaction", "automatic") === undefined}
            <div>
              <StudioButton type="button" onclick={ensureCompaction}>Enable automatic compaction</StudioButton>
            </div>
          {:else}
            <div class="grid gap-4 md:grid-cols-2">
              <StructuredField label="Trigger" type="select" value={valueAt("compaction", "automatic.trigger.$type")}
                options={[
                  { value: "turnCount", label: "Turn count" },
                  { value: "inputTokens", label: "Input tokens" },
                  { value: "contextPercentage", label: "Context percentage" },
                ]}
                onchange={value => change("compaction", "automatic.trigger.$type", value)} />
              <StructuredField label="Turn threshold" type="number" min={1}
                value={valueAt("compaction", "automatic.trigger.turns")}
                onchange={value => change("compaction", "automatic.trigger.turns", value)} />
              <StructuredField label="Input-token threshold" type="number" min={1}
                value={valueAt("compaction", "automatic.trigger.inputTokens")}
                onchange={value => change("compaction", "automatic.trigger.inputTokens", value)} />
              <StructuredField label="Context percentage" type="number" min={0} max={100}
                value={valueAt("compaction", "automatic.trigger.percentage")}
                onchange={value => change("compaction", "automatic.trigger.percentage", value)} />
              <StructuredField label="Strategy" type="select" value={valueAt("compaction", "automatic.compaction.strategy.$type")}
                options={[{ value: "removal", label: "Remove old history" }, { value: "summarizing", label: "Summarize" }]}
                onchange={value => change("compaction", "automatic.compaction.strategy.$type", value)} />
              <StructuredField label="Commit mode" type="select" value={valueAt("compaction", "automatic.compaction.commitMode")}
                options={[{ value: 0, label: "Soft" }, { value: 1, label: "Hard" }]}
                onchange={value => change("compaction", "automatic.compaction.commitMode", value)} />
              <StructuredField label="After compaction" type="select" value={valueAt("compaction", "automatic.continuation")}
                options={[{ value: 0, label: "Continue" }, { value: 1, label: "Stop after compaction" }]}
                onchange={value => change("compaction", "automatic.continuation", value)} />
              <StructuredField label="Historical preservation" type="select"
                value={valueAt("compaction", "automatic.compaction.preservation.$type")}
                options={[
                  { value: "none", label: "Preserve nothing" },
                  { value: "previousTurns", label: "Preserve turns" },
                  { value: "previousUserMessages", label: "Preserve user messages" },
                ]}
                onchange={value => change("compaction", "automatic.compaction.preservation.$type", value)} />
            </div>
          {/if}
        </div>
      {:else}
        {#if group.fields}
          <div class="mt-4 grid gap-4 md:grid-cols-2">
            {#each group.fields as field (field.path)}
              <StructuredField label={field.label} type={field.type} value={valueAt(group.key, field.path)}
                options={field.options} min={field.min} max={field.max} step={field.step}
                placeholder={field.placeholder} hint={field.hint}
                onchange={value => change(group.key, field.path, value)} />
            {/each}
          </div>
        {/if}
      {/if}

      {#if group.lists}
        <div class="mt-4 grid gap-4">
          {#each group.lists as list (list.path || list.label)}
            <section class="grid gap-3 border-t border-studio-line pt-4">
              <div class="flex flex-wrap items-start justify-between gap-2">
                <div><h4 class="font-semibold">{list.label}</h4><p class="text-body-sm text-studio-muted">{list.description}</p></div>
                <StudioButton type="button" size="small" onclick={() => addItem(group.key, list)}>Add {list.itemLabel}</StudioButton>
              </div>
              <div class="grid gap-3">
                {#each items(group.key, list.path) as item, index}
                  <article class="grid gap-3 rounded-studio border border-studio-line bg-studio-panel-soft p-3">
                    <div class="flex items-center justify-between gap-2">
                      <strong>{textItem(item, list)}</strong>
                      <StudioButton type="button" size="small" variant="secondary"
                        onclick={() => removeItem(group.key, list, index)}>Remove</StudioButton>
                    </div>
                    <div class="grid gap-3 md:grid-cols-2">
                      {#each list.fields as field (field.path)}
                        <StructuredField label={field.label} type={field.type}
                          value={itemValue(item, field.path)} options={field.options}
                          min={field.min} max={field.max} step={field.step}
                          placeholder={field.placeholder} hint={field.hint}
                          onchange={value => itemChange(group.key, list, index, field.path, value)} />
                      {/each}
                    </div>
                  </article>
                {:else}
                  <p class="rounded-studio border border-dashed border-studio-line p-3 text-body-sm text-studio-muted">No {list.itemLabel.toLowerCase()} is configured.</p>
                {/each}
              </div>
            </section>
          {/each}
        </div>
      {/if}

      <details class="mt-4 border-t border-studio-line pt-3">
        <summary class="cursor-pointer text-body-sm font-semibold text-studio-muted">Full section JSON</summary>
        <div class="mt-2">
          <StructuredField label="Bounded section JSON" type="json" value={rootValue(group.key)}
            hint="Use this only for provider-owned or polymorphic leaves not represented by the controls above."
            onchange={value => change(group.key, "", value)} />
        </div>
      </details>
    </details>
  {/each}
</div>
