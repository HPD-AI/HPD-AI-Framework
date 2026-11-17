<script lang="ts">
  import { onMount, untrack } from 'svelte';
  import {
    StudioButton,
    StudioNotice,
    StudioPageHeader,
    StudioPanel,
    StudioStatusBadge
  } from '@hpd-research/hpd-studio-design';
  import type { StudioOutwardResourceAuthority, StudioPageProps } from '@hpd-research/hpd-studio-core';
  import { readAction, readConfiguration, readConfigurationPreflight, readEventCatalog, readEventPayload, readExecution,
    readFunction, readLiveEvent, readMetrics, readSession, readThreadActivity, readThreadDetail, readToolHarness, readTrace } from './read-model.ts';
  import type { AgentPendingRequestProjection, AgentTraceEventProjection } from './read-model.ts';
  import AIFunctionCard from './AIFunctionCard.svelte';
  import CapabilityGraph from './CapabilityGraph.svelte';
  import { requireAgentStudioRuntime, type AgentStudioClient } from './runtime-context.ts';

  let { route, resource, commands }: StudioPageProps = $props();
  const agentRuntime = requireAgentStudioRuntime();
  const client: AgentStudioClient = agentRuntime.client;
  const pageId = $derived(route.routeId.endsWith('.route') ? route.routeId.slice(0, -6) : route.routeId);
  type SavedObservabilityView = Readonly<{
    id: string;
    name: string;
    section: string;
    query: Readonly<Record<string, string>>;
    createdAt: string;
  }>;
  const observabilityViewSection = $derived(
    pageId === "trace.catalog" || pageId === "trace.detail" ? "traces"
      : pageId === "observability.events" ? "events"
        : pageId === "observability.metrics" ? "metrics"
          : pageId === "observability.overview" ? "overview" : "");
  let phase: "loading" | "ready" | "failed" = $state("loading");
  let payload: unknown = $state(null);
  let failure: string = $state("");
  let savedViews = $state<SavedObservabilityView[]>([]);
  let savedViewName: string = $state("");
  let savedViewMessage: string = $state("");
  let harness = $derived(readToolHarness(payload));
  let functionProjection = $derived(readFunction(payload)[0] ?? null);
  let actionProjection = $derived(readAction(payload)[0] ?? null);
  let sessionProjection = $derived(readSession(payload));
  let threadProjection = $derived(readThreadDetail(payload));
  let executionProjection = $derived(readExecution(payload)[0] ?? null);
  let traceProjection = $derived(readTrace(payload));
  const initialRouteQuery = untrack(() => route.query);
  let traceAgentIdFilter: string = $state(text(initialRouteQuery.agentId));
  let traceSessionIdFilter: string = $state(text(initialRouteQuery.sessionId));
  let traceThreadIdFilter: string = $state(text(initialRouteQuery.threadId));
  let traceStateFilter: string = $state(text(initialRouteQuery.state));
  let traceAfterFilter: string = $state(routeMillisecondsToLocalInput(initialRouteQuery.after));
  let traceBeforeFilter: string = $state(routeMillisecondsToLocalInput(initialRouteQuery.before));
  let traceLimitFilter: string = $state(text(initialRouteQuery.limit) || "100");
  let traceTotal = $derived(recordNumber(payload, "total", rows(payload, "items").length));
  let traceOffset = $derived(recordNumber(payload, "offset", Number(text(route.query.offset) || "0")));
  let traceHasMore = $derived(recordBoolean(payload, "hasMore"));
  let metricsProjection = $derived(readMetrics(payload));
  let metricsAgentIdFilter: string = $state(text(initialRouteQuery.agentId));
  let metricsSessionIdFilter: string = $state(text(initialRouteQuery.sessionId));
  let metricsThreadIdFilter: string = $state(text(initialRouteQuery.threadId));
  let metricsStateFilter: string = $state(text(initialRouteQuery.state));
  let metricsAfterFilter: string = $state(routeMillisecondsToLocalInput(initialRouteQuery.after));
  let metricsBeforeFilter: string = $state(routeMillisecondsToLocalInput(initialRouteQuery.before));
  let eventCatalog = $derived(readEventCatalog(payload));
  let eventSearch: string = $state("");
  let eventAgentIdFilter: string = $state(text(initialRouteQuery.agentId));
  let eventSessionIdFilter: string = $state(text(initialRouteQuery.sessionId));
  let eventThreadIdFilter: string = $state(text(initialRouteQuery.threadId));
  let eventTraceIdFilter: string = $state(text(initialRouteQuery.traceId));
  let eventSpanIdFilter: string = $state(text(initialRouteQuery.spanId));
  let eventTypeFilter: string = $state(text(initialRouteQuery.type));
  let eventAfterFilter: string = $state(routeMillisecondsToLocalInput(initialRouteQuery.after));
  let eventBeforeFilter: string = $state(routeMillisecondsToLocalInput(initialRouteQuery.before));
  let eventErrorsOnly: boolean = $state(initialRouteQuery.errors === "true");
  let eventLimitFilter: string = $state(text(initialRouteQuery.limit) || "250");
  let eventTotal = $derived(recordNumber(payload, "total", eventCatalog?.count ?? 0));
  let eventOffset = $derived(recordNumber(payload, "offset", Number(text(initialRouteQuery.offset) || "0")));
  let eventHasMore = $derived(recordBoolean(payload, "hasMore"));
  let liveEvents = $state<AgentTraceEventProjection[]>([]);
  let liveEventCursor: string = $state("");
  let eventStreamPhase: "connecting" | "live" | "paused" | "failed" = $state("connecting");
  let eventTailPaused: boolean = $state(false);
  let eventStreamFailure: string = $state("");
  let eventSubscription: AbortController | null = null;
  let eventPayloadProjection = $state<ReturnType<typeof readEventPayload>>(null);
  let eventPayloadBusy: boolean = $state(false);
  let eventPayloadRequestId: string = $state("");
  let eventPayloadFailure: string = $state("");
  let filteredEvents = $derived([...liveEvents, ...(eventCatalog?.events ?? [])]
    .filter((event, index, values) => values.findIndex(value => value.id === event.id) === index)
    .filter(event => {
    const query = eventSearch.trim().toLowerCase();
    if (!query) return true;
    return [event.type, event.source, event.agentId, event.sessionId, event.threadId,
      event.traceId, event.spanId, ...Object.values(event.attributes)]
      .some(value => value.toLowerCase().includes(query));
  }));
  let configurationProjection = $derived(readConfiguration(payload));
  let draftJson: string = $state("");
  let draftPreflight = $state<ReturnType<typeof readConfigurationPreflight>>(null);
  let preflightBusy: boolean = $state(false);
  let preflightFailure: string = $state("");
  let commandState: unknown = $state({ kind: "closed" });
  let commandBusy: boolean = $state(false);
  let impactAccepted: boolean = $state(false);
  let commandFailure: string = $state("");
  let deleteAccepted: boolean = $state(false);
  let deleteRecursive: boolean = $state(false);
  let deleteBusy: boolean = $state(false);
  let deleteFailure: string = $state("");
  let commandKind = $derived(readCommandKind(commandState));
  let commandPreview = $derived(readCommandPreview(commandState));
  let workspaceSessions = $derived(rows(payload, "sessions"));
  let selectedSessionId: string = $state("");
  let sessionThreads: readonly Record<string, unknown>[] = $state([]);
  let selectedThreadId: string = $state("");
  let threadExecutions: readonly Record<string, unknown>[] = $state([]);
  let messageText: string = $state("");
  let newThreadName: string = $state("");
  let forkFromMessageId: string = $state("");
  let forkName: string = $state("");
  let workspaceBusy: boolean = $state(false);
  let workspaceFailure: string = $state("");
  let threadActivity = $derived(readThreadActivity(
    payload !== null && typeof payload === "object"
      ? (payload as Record<string, unknown>).activity
      : null));
  let activityPhase: "connecting" | "live" | "failed" = $state("connecting");
  let activityFailure: string = $state("");
  let activitySubscription: AbortController | null = null;
  let lastActiveExecutionId: string = "";
  let selectedSession = $derived(workspaceSessions.find(item => text(item.id) === selectedSessionId) ?? null);
  let selectedThread = $derived(sessionThreads.find(item => text(item.id) === selectedThreadId) ?? null);
  let activeExecution = $derived.by(() => threadExecutions.find(executionIsActive) ??
    (threadActivity?.activeExecutionId ? { id: threadActivity.activeExecutionId, state: "Active" } : null));
  let transcriptMessages = $derived(threadActivity?.messages ?? []);
  let recentActivityEvents = $derived(threadActivity?.events ?? []);
  let pendingRequests = $derived(threadActivity?.pendingRequests ?? []);
  let permissionFeedback: Record<string, string> = $state({});
  let questionAnswers = $state<Record<string, Record<string, {
    selected: string[];
    custom: string;
    notes: string;
  }>>>({});
  let continuationExtensions: Record<string, number> = $state({});
  let clientToolContent: Record<string, string> = $state({});
  let clientToolErrors: Record<string, string> = $state({});

  onMount(() => {
    const controller = new AbortController();
    const unsubscribe = commands.subscribe(value => commandState = value);
    savedViews = readSavedViews();
    void load(controller.signal);
    return () => {
      controller.abort();
      stopActivityStream();
      stopEventStream();
      unsubscribe();
      commands.close();
    };
  });

  async function load(signal: AbortSignal): Promise<void> {
    phase = "loading";
    failure = "";
    try {
      payload = await readPage(client, pageId, route.parameters, resource, signal);
      if (pageId === "agent.configuration") {
        const configuration = readConfiguration(payload);
        if (configuration) draftJson = JSON.stringify(configuration.canonicalConfig, null, 2);
      }
      if (pageId === "agent.workspace" && !signal.aborted) await initializeWorkspace(signal);
      if (!signal.aborted) {
        phase = "ready";
        if (pageId === "observability.events") startEventStream();
      }
    } catch (error) {
      if (signal.aborted) return;
      failure = error instanceof Error ? error.message : "Agent Studio observation failed.";
      phase = "failed";
    }
  }

  function rows(value: unknown, key = "items"): readonly Record<string, unknown>[] {
    if (!value || typeof value !== "object") return [];
    const candidate = (value as Record<string, unknown>)[key];
    return Array.isArray(candidate)
      ? candidate.filter((item): item is Record<string, unknown> => Boolean(item) && typeof item === "object")
      : [];
  }

  function text(value: unknown): string {
    return typeof value === "string" ? value : "";
  }

  function recordNumber(value: unknown, key: string, fallback: number): number {
    if (!value || typeof value !== "object") return fallback;
    const candidate = (value as Record<string, unknown>)[key];
    return typeof candidate === "number" && Number.isFinite(candidate) ? candidate : fallback;
  }

  function recordBoolean(value: unknown, key: string): boolean {
    if (!value || typeof value !== "object") return false;
    return (value as Record<string, unknown>)[key] === true;
  }

  function routeMillisecondsToLocalInput(value: string | undefined): string {
    if (!value || !/^\d+$/u.test(value)) return "";
    const date = new Date(Number(value));
    if (!Number.isFinite(date.getTime())) return "";
    const pad = (part: number): string => String(part).padStart(2, "0");
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  }

  function localInputToRouteMilliseconds(value: string): string {
    if (!value) return "";
    const milliseconds = new Date(value).getTime();
    return Number.isFinite(milliseconds) ? String(milliseconds) : "";
  }

  function applyTraceFilters(): void {
    const query: Record<string, string> = {};
    if (traceAgentIdFilter.trim()) query.agentId = traceAgentIdFilter.trim();
    if (traceSessionIdFilter.trim()) query.sessionId = traceSessionIdFilter.trim();
    if (traceThreadIdFilter.trim()) query.threadId = traceThreadIdFilter.trim();
    if (traceStateFilter) query.state = traceStateFilter;
    const after = localInputToRouteMilliseconds(traceAfterFilter);
    const before = localInputToRouteMilliseconds(traceBeforeFilter);
    if (after) query.after = after;
    if (before) query.before = before;
    if (traceLimitFilter) query.limit = traceLimitFilter;
    navigateTraceQuery(query);
  }

  function resetTraceFilters(): void {
    navigateTraceQuery({ limit: "100" });
  }

  function pageTraces(direction: -1 | 1): void {
    const limit = Number(traceLimitFilter) || 100;
    const nextOffset = Math.max(0, traceOffset + direction * limit);
    navigateTraceQuery({ ...route.query, offset: String(nextOffset) });
  }

  function applyEventFilters(): void {
    const query: Record<string, string> = {};
    if (eventAgentIdFilter.trim()) query.agentId = eventAgentIdFilter.trim();
    if (eventSessionIdFilter.trim()) query.sessionId = eventSessionIdFilter.trim();
    if (eventThreadIdFilter.trim()) query.threadId = eventThreadIdFilter.trim();
    if (eventTraceIdFilter.trim()) query.traceId = eventTraceIdFilter.trim();
    if (eventSpanIdFilter.trim()) query.spanId = eventSpanIdFilter.trim();
    if (eventTypeFilter.trim()) query.type = eventTypeFilter.trim();
    const after = localInputToRouteMilliseconds(eventAfterFilter);
    const before = localInputToRouteMilliseconds(eventBeforeFilter);
    if (after) query.after = after;
    if (before) query.before = before;
    if (eventErrorsOnly) query.errors = "true";
    if (eventLimitFilter) query.limit = eventLimitFilter;
    navigateObservabilityQuery("events", query);
  }

  function resetEventFilters(): void {
    navigateObservabilityQuery("events", { limit: "250" });
  }

  function startEventStream(): void {
    stopEventStream();
    eventTailPaused = false;
    eventStreamPhase = "live";
    eventStreamFailure = "";
    const controller = new AbortController();
    eventSubscription = controller;
    void runEventSubscription(controller);
  }

  async function runEventSubscription(controller: AbortController): Promise<void> {
    while (!controller.signal.aborted) {
      try {
        for await (const value of agentRuntime.subscribeEvents(controller.signal)) {
          if (controller.signal.aborted) return;
          const event = readLiveEvent(value);
          if (!event) continue;
          liveEvents = [event, ...liveEvents.filter(value => value.id !== event.id)].slice(0, 500);
          liveEventCursor = event.id;
          eventStreamPhase = "live";
          eventStreamFailure = "";
        }
        if (!controller.signal.aborted) eventStreamPhase = "connecting";
      } catch (error) {
        if (controller.signal.aborted) return;
        eventStreamPhase = "failed";
        eventStreamFailure = error instanceof Error ? error.message : "Live event observation failed.";
      }
      if (!controller.signal.aborted)
        await new Promise(resolve => setTimeout(resolve, 750));
    }
  }

  function stopEventStream(): void {
    eventSubscription?.abort();
    eventSubscription = null;
  }

  function toggleEventTail(): void {
    if (eventTailPaused) {
      startEventStream();
      return;
    }
    stopEventStream();
    eventTailPaused = true;
    eventStreamPhase = "paused";
  }

  async function inspectEventPayload(eventId: string): Promise<void> {
    if (eventPayloadBusy || !eventId) return;
    eventPayloadBusy = true;
    eventPayloadRequestId = eventId;
    eventPayloadFailure = "";
    try {
      const response = await client.getEventPayload(eventId, new AbortController().signal);
      const projection = readEventPayload(response);
      if (!projection) throw new TypeError("The server returned an invalid event payload projection.");
      eventPayloadProjection = projection;
    } catch (error) {
      eventPayloadProjection = null;
      eventPayloadFailure = commandFailureText(error, "Event payload inspection was denied or unavailable.");
    } finally {
      eventPayloadBusy = false;
      eventPayloadRequestId = "";
    }
  }

  function pageEvents(direction: -1 | 1): void {
    const limit = Number(eventLimitFilter) || 250;
    const nextOffset = Math.max(0, eventOffset + direction * limit);
    navigateObservabilityQuery("events", { ...route.query, offset: String(nextOffset) });
  }

  function applyMetricsFilters(): void {
    const query: Record<string, string> = {};
    if (metricsAgentIdFilter.trim()) query.agentId = metricsAgentIdFilter.trim();
    if (metricsSessionIdFilter.trim()) query.sessionId = metricsSessionIdFilter.trim();
    if (metricsThreadIdFilter.trim()) query.threadId = metricsThreadIdFilter.trim();
    if (metricsStateFilter) query.state = metricsStateFilter;
    const after = localInputToRouteMilliseconds(metricsAfterFilter);
    const before = localInputToRouteMilliseconds(metricsBeforeFilter);
    if (after) query.after = after;
    if (before) query.before = before;
    navigateObservabilityQuery("metrics", query);
  }

  function resetMetricsFilters(): void {
    navigateObservabilityQuery("metrics", {});
  }

  function navigateTraceQuery(query: Readonly<Record<string, string>>): void {
    navigateObservabilityQuery("traces", query);
  }

  function navigateObservabilityQuery(
    section: "events" | "metrics" | "traces", query: Readonly<Record<string, string>>
  ): void {
    const members = Object.entries(query)
      .filter(([, value]) => value.length > 0)
      .sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0)
      .map(([name, value]) => `${encodeURIComponent(name)}=${encodeURIComponent(value)}`);
    globalThis.location.assign(`/studio/operations/observability/${section}${members.length ? `?${members.join("&")}` : ""}`);
  }

  function savedViewsStorageKey(): string {
    return `hpd.agent.studio.saved-views.v1.${agentRuntime.principalGeneration}`;
  }

  function readSavedViews(): SavedObservabilityView[] {
    try {
      const raw = globalThis.localStorage.getItem(savedViewsStorageKey());
      if (!raw) return [];
      const values = JSON.parse(raw) as unknown;
      if (!Array.isArray(values)) return [];
      return values.flatMap(value => {
        if (!value || typeof value !== "object") return [];
        const item = value as Record<string, unknown>;
        const id = text(item.id);
        const name = text(item.name);
        const section = text(item.section);
        const createdAt = text(item.createdAt);
        const query = item.query !== null && typeof item.query === "object"
          ? Object.fromEntries(Object.entries(item.query as Record<string, unknown>)
              .flatMap(([key, member]) => typeof member === "string" ? [[key, member]] : []))
          : {};
        return id && name && section === observabilityViewSection
          ? [{ id, name, section, query, createdAt }]
          : [];
      }).slice(0, 32);
    } catch {
      return [];
    }
  }

  function saveCurrentView(): void {
    const name = savedViewName.trim();
    if (!observabilityViewSection || name.length < 1 || name.length > 64) {
      savedViewMessage = "Enter a saved-view name up to 64 characters.";
      return;
    }
    const all = readAllSavedViews();
    const value: SavedObservabilityView = Object.freeze({
      id: globalThis.crypto?.randomUUID?.() ?? `view-${Date.now()}`,
      name,
      section: observabilityViewSection,
      query: Object.freeze({ ...route.query }),
      createdAt: new Date().toISOString()
    });
    const retained = all.filter(item =>
      item.section !== value.section || item.name.toLowerCase() !== value.name.toLowerCase());
    retained.unshift(value);
    persistSavedViews(retained.slice(0, 32));
    savedViews = retained.filter(item => item.section === value.section).slice(0, 32);
    savedViewName = "";
    savedViewMessage = `Saved “${value.name}”.`;
  }

  function applySavedView(view: SavedObservabilityView): void {
    const members = Object.entries(view.query)
      .sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0)
      .map(([name, value]) => `${encodeURIComponent(name)}=${encodeURIComponent(value)}`);
    globalThis.location.assign(`/studio/operations/observability/${view.section}${members.length ? `?${members.join("&")}` : ""}`);
  }

  function deleteSavedView(id: string): void {
    const retained = readAllSavedViews().filter(item => item.id !== id);
    persistSavedViews(retained);
    savedViews = retained.filter(item => item.section === observabilityViewSection);
    savedViewMessage = "Saved view deleted.";
  }

  function readAllSavedViews(): SavedObservabilityView[] {
    try {
      const raw = globalThis.localStorage.getItem(savedViewsStorageKey());
      const parsed = raw ? JSON.parse(raw) as unknown : [];
      if (!Array.isArray(parsed)) return [];
      return parsed.flatMap(value => {
        if (!value || typeof value !== "object") return [];
        const item = value as Record<string, unknown>;
        const id = text(item.id);
        const name = text(item.name);
        const section = text(item.section);
        const query = item.query !== null && typeof item.query === "object"
          ? Object.fromEntries(Object.entries(item.query as Record<string, unknown>)
              .flatMap(([key, member]) => typeof member === "string" ? [[key, member]] : []))
          : {};
        return id && name && section
          ? [{ id, name, section, query, createdAt: text(item.createdAt) }]
          : [];
      });
    } catch {
      return [];
    }
  }

  function persistSavedViews(values: readonly SavedObservabilityView[]): void {
    try {
      globalThis.localStorage.setItem(savedViewsStorageKey(), JSON.stringify(values.slice(0, 32)));
      savedViewMessage = "";
    } catch {
      savedViewMessage = "Saved views could not be stored on this device.";
    }
  }

  function exportCurrentProjection(): void {
    if (!observabilityViewSection) return;
    const payloadRecord = payload !== null && typeof payload === "object"
      ? payload as Record<string, unknown>
      : { value: payload };
    const projection = pageId === "observability.events"
      ? {
          ...payloadRecord,
          items: [...liveEvents, ...rows(payload)]
            .filter((event, index, values) =>
              values.findIndex(value => value.id === event.id) === index)
        }
      : payloadRecord;
    const artifact = JSON.stringify({
      state: "authorized-export",
      section: observabilityViewSection,
      pageId,
      exportedAt: new Date().toISOString(),
      route: { query: route.query },
      projection,
      selectedEventPayload: pageId === "observability.events" ? eventPayloadProjection : null
    }, null, 2);
    const blob = new Blob([artifact], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `hpd-agent-${observabilityViewSection}-${new Date().toISOString().replaceAll(":", "-")}.json`;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  function currentAgent(): StudioOutwardResourceAuthority | null {
    return resource?.kind === "agent" ? resource : null;
  }

  function agentId(): string {
    const value = currentAgent()?.agentId;
    return typeof value === "string" ? value : "";
  }

  function agentLink(token: unknown): string {
    return typeof token === "string" ? `/studio/operations/agents/${encodeURIComponent(token)}` : "#";
  }

  function title(): string {
    return ({
      "agent.catalog": "Agents",
      "agent.overview": text(currentAgent()?.name) || agentId() || "Agent",
      "agent.workspace": "Chat workspace",
      "agent.configuration": "Configuration",
      "harness.catalog": "ToolHarnesses",
      "harness.detail": harness?.name || text(resource?.harnessId) || "ToolHarness",
      "aiFunction.detail": functionProjection?.name || text(resource?.functionId) || "AIFunction",
      "aiFunction.action.detail": actionProjection?.name || text(resource?.actionId) || "AIFunctionAction",
      "session.catalog": "Sessions",
      "session.detail": text(resource?.sessionId) || "Session",
      "thread.detail": text(resource?.threadId) || "Thread",
      "execution.detail": text(resource?.threadExecutionId) || "Execution",
      "observability.overview": "Observability",
      "observability.metrics": "Metrics",
      "observability.events": "Events & logs",
      "trace.catalog": "Traces",
      "trace.detail": traceProjection?.name || text(resource?.traceId) || "Trace",
    } as Record<string, string>)[pageId] ?? "Agent Studio";
  }

  async function readPage(
    client: AgentStudioClient,
    pageId: string,
    parameters: Readonly<Record<string, string>>,
    resource: StudioOutwardResourceAuthority | null,
    signal: AbortSignal,
  ): Promise<unknown> {
    if (pageId === "agent.catalog") return client.listAgents(signal);
    if (pageId === "agent.overview")
      return client.getAgent(requiredText(resource, "agentId"), signal);
    if (pageId === "agent.workspace") {
      const agentId = requiredText(resource, "agentId");
      const [agent, sessions] = await Promise.all([
        client.getAgent(agentId, signal),
        client.listAgentSessions(agentId, signal),
      ]);
      return { ...(agent as object), sessions: rows(sessions) };
    }
    if (pageId === "agent.configuration")
      return client.getAgentConfiguration(requiredText(resource, "agentId"), signal);
    if (pageId === "harness.catalog") return client.listHarnesses(signal);
    if (pageId === "harness.detail") return client.getHarness(requiredText(resource, "harnessId"), signal);
    if (pageId === "aiFunction.detail")
      return client.getFunction(requiredText(resource, "harnessId"), requiredText(resource, "functionId"), signal);
    if (pageId === "aiFunction.action.detail")
      return client.getFunctionAction(requiredText(resource, "harnessId"), requiredText(resource, "functionId"),
        requiredText(resource, "actionId"), signal);
    if (pageId === "session.catalog") return client.listSessions(signal);
    if (pageId === "session.detail") {
      const sessionId = requiredText(resource, "sessionId");
      const [session, threads] = await Promise.all([client.getSession(sessionId, signal), client.listThreads(sessionId, signal)]);
      return { ...(session as object), threads: rows(threads) };
    }
    if (pageId === "thread.detail") {
      const sessionId = requiredText(resource, "sessionId");
      const threadId = requiredText(resource, "threadId");
      const response = await client.getThread(sessionId, threadId, signal) as Record<string, unknown>;
      const thread = (response.thread !== null && typeof response.thread === "object"
        ? response.thread : response) as Record<string, unknown>;
      const agentId = text(thread.defaultAgentId);
      const [threads, executions] = await Promise.all([
        client.listThreads(sessionId, signal),
        agentId ? client.listExecutions(agentId, sessionId, threadId, signal) : Promise.resolve({ items: [] })
      ]);
      return {
        ...thread,
        childThreads: rows(threads).filter(item => item.parentThreadId === threadId),
        executions: rows(executions)
      };
    }
    if (pageId === "execution.detail") {
      const sessionId = requiredText(resource, "sessionId");
      const threadId = requiredText(resource, "threadId");
      const executionId = requiredText(resource, "threadExecutionId");
      const response = await client.getThread(sessionId, threadId, signal) as Record<string, unknown>;
      const thread = (response.thread !== null && typeof response.thread === "object"
        ? response.thread : response) as Record<string, unknown>;
      const execution = await client.getExecution(String(thread.defaultAgentId), sessionId, threadId, executionId, signal);
      return { ...(execution as object), sessionId, threadId, defaultAgentId: text(thread.defaultAgentId) };
    }
    if (pageId === "observability.overview") return client.observabilitySummary(signal);
    if (pageId === "observability.metrics") return client.queryMetrics(route.query, signal);
    if (pageId === "observability.events") return client.listEvents(route.query, signal);
    if (pageId === "trace.catalog") return client.listTraces(route.query, signal);
    if (pageId === "trace.detail") return client.getTrace(requiredText(resource, "traceId"), signal);
    throw new TypeError(`Agent Studio page '${pageId}' is not registered. ${parameters.resource ?? ""}`);
  }

  function requiredText(resource: StudioOutwardResourceAuthority | null, name: string): string {
    const value = resource?.[name];
    if (typeof value !== "string" || value.length === 0) throw new TypeError(`Agent Studio resource '${name}' is unavailable.`);
    return value;
  }

  function studioLink(section: string, token: string | null | undefined): string {
    return token ? `/studio/operations/${section}/${encodeURIComponent(token)}` : "#";
  }

  function permissionTone(kind: string): "neutral" | "info" | "warning" | "danger" | "success" {
    if (kind.toLowerCase() === "deny") return "danger";
    if (kind.toLowerCase() === "ask") return "warning";
    if (kind.toLowerCase() === "allow") return "success";
    return "neutral";
  }

  async function initializeWorkspace(signal: AbortSignal): Promise<void> {
    const available = rows(payload, "sessions");
    const retained = available.find(item => text(item.id) === selectedSessionId) ?? available[0];
    selectedSessionId = retained ? text(retained.id) : "";
    if (selectedSessionId) await loadSession(selectedSessionId, signal);
    else {
      sessionThreads = [];
      selectedThreadId = "";
      threadExecutions = [];
    }
  }

  async function refreshWorkspaceSessions(signal = new AbortController().signal): Promise<void> {
    const sessions = await client.listAgentSessions(agentId(), signal);
    payload = { ...(payload as Record<string, unknown>), sessions: rows(sessions) };
  }

  async function loadSession(sessionId: string, signal: AbortSignal): Promise<void> {
    selectedSessionId = sessionId;
    lastActiveExecutionId = "";
    workspaceFailure = "";
    try {
      const threads = await client.listThreads(sessionId, signal);
      sessionThreads = rows(threads);
      const retained = sessionThreads.find(item => text(item.id) === selectedThreadId);
      const main = sessionThreads.find(item => text(item.kind) === "MainAgent" && !item.parentThreadId);
      selectedThreadId = text(retained?.id) || text(main?.id) || text(sessionThreads[0]?.id);
      if (selectedThreadId) await loadExecutions(sessionId, selectedThreadId, signal);
      else threadExecutions = [];
      payload = { ...(payload as Record<string, unknown>), activity: null };
      if (selectedThreadId) {
        await loadThreadActivity(sessionId, selectedThreadId, signal);
        startActivityStream(sessionId, selectedThreadId);
      } else {
        stopActivityStream();
      }
    } catch (error) {
      if (signal.aborted) return;
      workspaceFailure = error instanceof Error ? error.message : "Session observation failed.";
      sessionThreads = [];
      selectedThreadId = "";
      threadExecutions = [];
      stopActivityStream();
    }
  }

  async function chooseSession(event: Event): Promise<void> {
    const sessionId = (event.currentTarget as HTMLSelectElement).value;
    await loadSession(sessionId, new AbortController().signal);
  }

  async function chooseThread(threadId: string): Promise<void> {
    selectedThreadId = threadId;
    workspaceFailure = "";
    try {
      await loadExecutions(selectedSessionId, threadId, new AbortController().signal);
      await loadThreadActivity(selectedSessionId, threadId, new AbortController().signal);
      startActivityStream(selectedSessionId, threadId);
    } catch (error) {
      workspaceFailure = error instanceof Error ? error.message : "Thread execution observation failed.";
      threadExecutions = [];
      stopActivityStream();
    }
  }

  async function loadExecutions(sessionId: string, threadId: string, signal: AbortSignal): Promise<void> {
    const executions = await client.listExecutions(agentId(), sessionId, threadId, signal);
    threadExecutions = rows(executions).slice().sort((left, right) =>
      text(right.startedAt).localeCompare(text(left.startedAt)));
  }

  async function createWorkspaceSession(): Promise<void> {
    if (workspaceBusy || !currentAgent()) return;
    workspaceBusy = true;
    workspaceFailure = "";
    try {
      const result = await performWorkspaceCommand("agent.session.create", Object.freeze({}));
      await refreshWorkspaceSessions();
      const sessionId = text(result.sessionId) || selectedSessionId;
      await loadSession(sessionId, new AbortController().signal);
    } catch (error) {
      workspaceFailure = commandFailureText(error, "Session creation failed.");
    } finally {
      workspaceBusy = false;
    }
  }

  async function createWorkspaceThread(): Promise<void> {
    if (workspaceBusy || !currentAgent() || !selectedSessionId) return;
    const name = newThreadName.trim() || `thread-${sessionThreads.length + 1}`;
    workspaceBusy = true;
    workspaceFailure = "";
    try {
      const result = await performWorkspaceCommand("agent.thread.create", Object.freeze({
        sessionId: selectedSessionId,
        name,
      }));
      newThreadName = "";
      const threads = await client.listThreads(selectedSessionId, new AbortController().signal);
      sessionThreads = rows(threads);
      selectedThreadId = text(result.threadId);
      await loadExecutions(selectedSessionId, selectedThreadId, new AbortController().signal);
      await loadThreadActivity(selectedSessionId, selectedThreadId, new AbortController().signal);
      startActivityStream(selectedSessionId, selectedThreadId);
    } catch (error) {
      workspaceFailure = commandFailureText(error, "Thread creation failed.");
    } finally {
      workspaceBusy = false;
    }
  }

  async function forkWorkspaceThread(): Promise<void> {
    if (workspaceBusy || !currentAgent() || !selectedSessionId || !selectedThreadId) return;
    const name = forkName.trim() || `${text(selectedThread?.name) || selectedThreadId}-fork`;
    workspaceBusy = true;
    workspaceFailure = "";
    try {
      const result = await performWorkspaceCommand("agent.thread.fork", Object.freeze({
        sessionId: selectedSessionId,
        threadId: selectedThreadId,
        fromMessageId: forkFromMessageId || null,
        name,
      }));
      forkName = "";
      const threads = await client.listThreads(selectedSessionId, new AbortController().signal);
      sessionThreads = rows(threads);
      selectedThreadId = text(result.threadId);
      await loadExecutions(selectedSessionId, selectedThreadId, new AbortController().signal);
      await loadThreadActivity(selectedSessionId, selectedThreadId, new AbortController().signal);
      startActivityStream(selectedSessionId, selectedThreadId);
    } catch (error) {
      workspaceFailure = commandFailureText(error, "Thread fork failed.");
    } finally {
      workspaceBusy = false;
    }
  }

  async function submitWorkspaceInput(): Promise<void> {
    if (workspaceBusy || !currentAgent() || !selectedSessionId || !selectedThreadId || !messageText.trim()) return;
    workspaceBusy = true;
    workspaceFailure = "";
    try {
      await performWorkspaceCommand("agent.input.submit", Object.freeze({
        sessionId: selectedSessionId,
        threadId: selectedThreadId,
        text: messageText.trim(),
      }));
      messageText = "";
      await loadExecutions(selectedSessionId, selectedThreadId, new AbortController().signal);
      await refreshWorkspaceSessions();
      await loadThreadActivity(selectedSessionId, selectedThreadId, new AbortController().signal);
    } catch (error) {
      workspaceFailure = commandFailureText(error, "Input submission failed.");
    } finally {
      workspaceBusy = false;
    }
  }

  async function steerWorkspaceInput(): Promise<void> {
    if (workspaceBusy || !currentAgent() || !selectedSessionId || !selectedThreadId ||
        !activeExecution || !messageText.trim()) return;
    workspaceBusy = true;
    workspaceFailure = "";
    try {
      await performWorkspaceCommand("agent.input.steer", Object.freeze({
        sessionId: selectedSessionId,
        threadId: selectedThreadId,
        threadExecutionId: text(activeExecution.id),
        text: messageText.trim(),
      }));
      messageText = "";
      await loadThreadActivity(selectedSessionId, selectedThreadId, new AbortController().signal);
    } catch (error) {
      workspaceFailure = commandFailureText(error, "Execution steering failed.");
    } finally {
      workspaceBusy = false;
    }
  }

  async function respondToPermission(request: AgentPendingRequestProjection, choiceId: string): Promise<void> {
    if (workspaceBusy || !currentAgent() || !selectedSessionId || !selectedThreadId) return;
    workspaceBusy = true;
    workspaceFailure = "";
    try {
      await performWorkspaceCommand("agent.permission.respond", Object.freeze({
        sessionId: selectedSessionId,
        threadId: selectedThreadId,
        requestId: request.requestId,
        sourceName: request.sourceName,
        choiceId,
        feedback: permissionFeedback[request.requestId]?.trim() || null,
      }));
      delete permissionFeedback[request.requestId];
      await loadThreadActivity(selectedSessionId, selectedThreadId, new AbortController().signal);
    } catch (error) {
      workspaceFailure = commandFailureText(error, "Permission response failed.");
    } finally {
      workspaceBusy = false;
    }
  }

  async function respondToQuestion(request: AgentPendingRequestProjection,
    outcome: "answered" | "dismissed" | "discuss"): Promise<void> {
    if (workspaceBusy || !currentAgent() || !selectedSessionId || !selectedThreadId) return;
    workspaceBusy = true;
    workspaceFailure = "";
    try {
      const answers = outcome === "answered"
        ? request.questions.map(question => {
            const answer = questionAnswers[request.requestId]?.[question.id];
            return {
              questionId: question.id,
              selectedOptionIds: answer?.selected ?? [],
              customText: answer?.custom.trim() || null,
              notes: answer?.notes.trim() || null,
            };
          })
        : [];
      await performWorkspaceCommand("agent.question.respond", Object.freeze({
        sessionId: selectedSessionId,
        threadId: selectedThreadId,
        requestId: request.requestId,
        sourceName: request.sourceName,
        outcome,
        answersJson: JSON.stringify(answers),
      }));
      delete questionAnswers[request.requestId];
      await loadThreadActivity(selectedSessionId, selectedThreadId, new AbortController().signal);
    } catch (error) {
      workspaceFailure = commandFailureText(error, "Question response failed.");
    } finally {
      workspaceBusy = false;
    }
  }

  async function respondToContinuation(request: AgentPendingRequestProjection, approved: boolean): Promise<void> {
    if (workspaceBusy || !currentAgent() || !selectedSessionId || !selectedThreadId) return;
    workspaceBusy = true;
    workspaceFailure = "";
    try {
      await performWorkspaceCommand("agent.continuation.respond", Object.freeze({
        sessionId: selectedSessionId,
        threadId: selectedThreadId,
        requestId: request.requestId,
        sourceName: request.sourceName,
        approved,
        extensionAmount: approved ? continuationExtensions[request.requestId] ?? 1 : 0,
      }));
      delete continuationExtensions[request.requestId];
      await loadThreadActivity(selectedSessionId, selectedThreadId, new AbortController().signal);
    } catch (error) {
      workspaceFailure = commandFailureText(error, "Continuation response failed.");
    } finally {
      workspaceBusy = false;
    }
  }

  async function respondToClientTool(request: AgentPendingRequestProjection,
    outcome: "Completed" | "Rejected" | "Failed"): Promise<void> {
    if (workspaceBusy || !currentAgent() || !selectedSessionId || !selectedThreadId) return;
    workspaceBusy = true;
    workspaceFailure = "";
    try {
      await performWorkspaceCommand("agent.client-tool.respond", Object.freeze({
        sessionId: selectedSessionId,
        threadId: selectedThreadId,
        requestId: request.requestId,
        outcome,
        contentText: outcome === "Completed" ? clientToolContent[request.requestId]?.trim() || null : null,
        errorMessage: outcome === "Completed" ? null : clientToolErrors[request.requestId]?.trim() || null,
      }));
      delete clientToolContent[request.requestId];
      delete clientToolErrors[request.requestId];
      await loadThreadActivity(selectedSessionId, selectedThreadId, new AbortController().signal);
    } catch (error) {
      workspaceFailure = commandFailureText(error, "Client-tool response failed.");
    } finally {
      workspaceBusy = false;
    }
  }

  async function cancelActiveExecution(): Promise<void> {
    if (workspaceBusy || !currentAgent() || !activeExecution || !selectedSessionId || !selectedThreadId) return;
    workspaceBusy = true;
    workspaceFailure = "";
    try {
      await performWorkspaceCommand("agent.execution.cancel", Object.freeze({
        sessionId: selectedSessionId,
        threadId: selectedThreadId,
        threadExecutionId: text(activeExecution.id),
      }));
      await loadExecutions(selectedSessionId, selectedThreadId, new AbortController().signal);
    } catch (error) {
      workspaceFailure = commandFailureText(error, "Execution cancellation failed.");
    } finally {
      workspaceBusy = false;
    }
  }

  async function performWorkspaceCommand(commandId: string, input: Readonly<Record<string, unknown>>):
    Promise<Record<string, unknown>> {
    commands.open(commandId, currentAgent()!, input);
    await commands.preview();
    if (readCommandKind(commandState) !== "review")
      throw new TypeError("The server rejected the command review.");
    await commands.execute();
    const kind = readCommandKind(commandState);
    const commandResult = commandState !== null && typeof commandState === "object"
      ? commandState as Record<string, unknown>
      : null;
    const rawConfirmed = text(commandResult?.mode) === "execute" &&
      typeof commandResult?.previewChecksum === "string";
    if (kind !== "confirmed" && kind !== "duplicate" && !rawConfirmed)
      throw new TypeError("The command did not reach a confirmed outcome.");
    const result = commandResult?.result ?? commandResult;
    commands.close();
    return result !== null && typeof result === "object"
      ? result as Record<string, unknown>
      : Object.freeze({});
  }

  function commandFailureText(error: unknown, fallback: string): string {
    return error instanceof Error ? error.message : fallback;
  }

  function executionIsActive(value: Record<string, unknown>): boolean {
    return text(value.state).toLowerCase() === "active";
  }

  async function loadThreadActivity(sessionId: string, threadId: string, signal: AbortSignal): Promise<void> {
    if (!sessionId || !threadId) return;
    try {
      const response = await client.getThreadActivity(agentId(), sessionId, threadId, signal);
      if (selectedSessionId !== sessionId || selectedThreadId !== threadId || signal.aborted) return;
      const projection = readThreadActivity(response);
      if (!projection) throw new TypeError("The server returned an invalid thread activity projection.");
      payload = { ...(payload as Record<string, unknown>), activity: response };
      activityPhase = "live";
      activityFailure = "";
      synchronizePendingAnswers(projection.pendingRequests);
      if (!projection.messages.some(message => message.id === forkFromMessageId))
        forkFromMessageId = projection.messages.at(-1)?.id ?? "";
      const observedActive = projection.activeExecutionId ?? "";
      if (observedActive !== lastActiveExecutionId) {
        lastActiveExecutionId = observedActive;
        await loadExecutions(sessionId, threadId, signal);
      }
    } catch (error) {
      if (signal.aborted) return;
      activityPhase = "failed";
      activityFailure = error instanceof Error ? error.message : "Live thread catch-up failed.";
    }
  }

  function startActivityStream(sessionId: string, threadId: string): void {
    stopActivityStream();
    if (!sessionId || !threadId) return;
    activityPhase = "connecting";
    const controller = new AbortController();
    activitySubscription = controller;
    void runActivitySubscription(sessionId, threadId, controller);
  }

  async function runActivitySubscription(sessionId: string, threadId: string,
    controller: AbortController): Promise<void> {
    while (!controller.signal.aborted) {
      try {
        for await (const _event of agentRuntime.subscribeThreadActivity(
          currentAgent(), sessionId, threadId, controller.signal)) {
          if (controller.signal.aborted) return;
          await loadThreadActivity(sessionId, threadId, controller.signal);
        }
        if (!controller.signal.aborted) activityPhase = "connecting";
      } catch (error) {
        if (controller.signal.aborted) return;
        activityPhase = "failed";
        activityFailure = error instanceof Error ? error.message : "Realtime thread observation failed.";
      }
      if (!controller.signal.aborted)
        await new Promise(resolve => setTimeout(resolve, 750));
    }
  }

  function stopActivityStream(): void {
    activitySubscription?.abort();
    activitySubscription = null;
  }

  function transcriptRole(value: unknown): string {
    const role = text(value).toLowerCase();
    return role || "unknown";
  }

  function transcriptLabel(message: { role: unknown; authorName: unknown }): string {
    const role = transcriptRole(message.role);
    return text(message.authorName) || (role === "user" ? "You" : role);
  }

  function shortTimestamp(value: unknown): string {
    const raw = text(value);
    if (!raw) return "";
    const parsed = new Date(raw);
    return Number.isNaN(parsed.valueOf()) ? raw : parsed.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
  }

  function messageOptionLabel(message: { role: unknown; text: unknown }): string {
    const role = transcriptRole(message.role);
    const summary = text(message.text).replace(/\s+/gu, " ").slice(0, 56);
    return `${role} · ${summary || "non-text content"}`;
  }

  function synchronizePendingAnswers(requests: readonly AgentPendingRequestProjection[]): void {
    const next = { ...questionAnswers };
    for (const request of requests.filter(item => item.kind === "question")) {
      const prior = next[request.requestId] ?? {};
      next[request.requestId] = Object.fromEntries(request.questions.map(question => [
        question.id,
        prior[question.id] ?? { selected: [], custom: "", notes: "" },
      ]));
    }
    questionAnswers = next;
  }

  function questionAnswer(requestId: string, questionId: string): {
    selected: string[];
    custom: string;
    notes: string;
  } {
    return questionAnswers[requestId]?.[questionId] ?? { selected: [], custom: "", notes: "" };
  }

  function toggleQuestionOption(requestId: string, questionId: string, optionId: string,
    multiple: boolean, selected: boolean): void {
    const current = questionAnswer(requestId, questionId);
    const ids = multiple
      ? selected
        ? [...new Set([...current.selected, optionId])]
        : current.selected.filter(value => value !== optionId)
      : selected ? [optionId] : [];
    questionAnswers = {
      ...questionAnswers,
      [requestId]: {
        ...questionAnswers[requestId],
        [questionId]: { ...current, selected: ids },
      },
    };
  }

  function setQuestionText(requestId: string, questionId: string,
    field: "custom" | "notes", value: string): void {
    const current = questionAnswer(requestId, questionId);
    questionAnswers = {
      ...questionAnswers,
      [requestId]: {
        ...questionAnswers[requestId],
        [questionId]: { ...current, [field]: value },
      },
    };
  }

  async function runConfigurationPreflight(): Promise<void> {
    if (preflightBusy) return;
    preflightBusy = true;
    preflightFailure = "";
    try {
      const draft = JSON.parse(draftJson) as unknown;
      const result = await client.preflightAgentConfiguration(requiredText(resource, "agentId"), draft);
      const projection = readConfigurationPreflight(result);
      if (!projection) throw new TypeError("The server returned an invalid preflight response.");
      draftPreflight = projection;
    } catch (error) {
      preflightFailure = error instanceof Error ? error.message : "Configuration preflight failed.";
    } finally {
      preflightBusy = false;
    }
  }

  async function reviewConfigurationChange(): Promise<void> {
    if (commandBusy || !currentAgent() || !draftPreflight || draftPreflight.state !== "valid") return;
    commandBusy = true;
    commandFailure = "";
    impactAccepted = false;
    try {
      commands.open("agent.configuration.update", currentAgent()!, Object.freeze({ configJson: draftJson }));
      await commands.preview();
    } catch (error) {
      commandFailure = error instanceof Error ? error.message : "Configuration review failed.";
    } finally {
      commandBusy = false;
    }
  }

  async function executeConfigurationChange(): Promise<void> {
    if (commandBusy || commandKind !== "review") return;
    commandBusy = true;
    commandFailure = "";
    try {
      await commands.execute();
      if (readCommandKind(commandState) === "confirmed" || readCommandKind(commandState) === "duplicate") {
        commands.close();
        impactAccepted = false;
        await load(new AbortController().signal);
      }
    } catch (error) {
      commandFailure = error instanceof Error ? error.message : "Configuration update failed.";
    } finally {
      commandBusy = false;
    }
  }

  function acknowledgeConfigurationImpact(accepted: boolean): void {
    impactAccepted = accepted;
    const acknowledgement = commandPreview?.acknowledgements[0];
    if (acknowledgement) {
      commands.acknowledge(`${acknowledgement.purposeId}\0${acknowledgement.impactId}`, accepted);
    }
  }

  function deletionCommandId(): string | null {
    if (pageId === "session.detail") return "agent.session.delete";
    if (pageId === "thread.detail") return "agent.thread.delete";
    return null;
  }

  function deletionTarget(): StudioOutwardResourceAuthority | null {
    if (pageId === "session.detail" && resource?.kind === "agentSession") return resource;
    if (pageId === "thread.detail" && resource?.kind === "agentThread") return resource;
    return null;
  }

  function setDeleteRecursive(value: boolean): void {
    if (deleteRecursive === value) return;
    deleteRecursive = value;
    if (commandKind === "review" || commandKind === "draft" || commandKind === "previewing") {
      commands.close();
      deleteAccepted = false;
      deleteFailure = "The deletion scope changed. Review the command again.";
    }
  }

  async function reviewDeletion(): Promise<void> {
    if (deleteBusy || !deletionTarget()) return;
    const commandId = deletionCommandId();
    const target = deletionTarget();
    if (!commandId || !target) return;
    deleteBusy = true;
    deleteAccepted = false;
    deleteFailure = "";
    try {
      commands.open(commandId, target, pageId === "thread.detail"
        ? Object.freeze({ recursive: deleteRecursive })
        : Object.freeze({}));
      await commands.preview();
      if (readCommandKind(commandState) !== "review") {
        commands.close();
        deleteFailure = "The server rejected the deletion review. Refresh the resource before trying again.";
      }
    } catch (error) {
      commands.close();
      deleteFailure = commandFailureText(error, "Deletion review failed.");
    } finally {
      deleteBusy = false;
    }
  }

  function acknowledgeDeletionImpact(accepted: boolean): void {
    deleteAccepted = accepted;
    const acknowledgement = commandPreview?.acknowledgements[0];
    if (acknowledgement) {
      commands.acknowledge(`${acknowledgement.purposeId}\0${acknowledgement.impactId}`, accepted);
    }
  }

  async function executeDeletion(): Promise<void> {
    if (deleteBusy || commandKind !== "review" || !deleteAccepted) return;
    deleteBusy = true;
    deleteFailure = "";
    try {
      await commands.execute();
      const kind = readCommandKind(commandState);
      if (kind === "confirmed" || kind === "duplicate") {
        const deletedSessionId = text(resource?.sessionId);
        commands.close();
        deleteAccepted = false;
        if (pageId === "session.detail") {
          globalThis.location.assign("/studio/operations/sessions");
        } else {
          await returnAfterThreadDeletion(deletedSessionId);
        }
        return;
      }
      deleteFailure = deletionCommandFailure(kind);
    } catch (error) {
      deleteFailure = commandFailureText(error, "Deletion did not complete.");
    } finally {
      deleteBusy = false;
    }
  }

  async function returnAfterThreadDeletion(sessionId: string): Promise<void> {
    if (sessionId) {
      try {
        const response = await client.getSession(sessionId, new AbortController().signal) as Record<string, unknown>;
        const resourceToken = text(response.resourceToken);
        if (resourceToken) {
          globalThis.location.assign(`/studio/operations/sessions/${encodeURIComponent(resourceToken)}`);
          return;
        }
      } catch {
        // The session may no longer be disclosed; the catalog is the safe parent destination.
      }
    }
    globalThis.location.assign("/studio/operations/sessions");
  }

  function deletionCommandFailure(kind: string): string {
    if (kind === "freshAuthenticationUnsupported")
      return "This host authentication integration does not support the multi-factor fresh authentication required for deletion.";
    if (kind === "freshAuthenticationRequired")
      return "Complete the fresh authentication challenge, then execute the same reviewed deletion.";
    if (kind === "retryable")
      return "The host rejected the deletion before influence. Review the current resource state before retrying.";
    if (kind === "unresolved")
      return "Deletion outcome is indeterminate. Do not retry blindly; refresh and verify whether the resource still exists.";
    if (kind === "failed")
      return "Deletion did not pass its checksum, review, or host lifecycle validation.";
    return "Deletion did not reach a confirmed outcome.";
  }

  function readCommandKind(value: unknown): string {
    if (value === null || typeof value !== "object") return "closed";
    const state = value as { kind?: unknown; mode?: unknown };
    if (typeof state.kind === "string") return state.kind;
    return state.mode === "execute" ? "confirmed" : "closed";
  }

  function readCommandPreview(value: unknown): {
    diff: readonly { path: string; change: string; before: string | null; after: string | null }[];
    acknowledgements: readonly { purposeId: string; impactId: string }[];
    changeCount: number;
    previewChecksum: string;
    expiresAtUtc: string;
    agentId: string;
    name: string;
    configChecksum: string;
    created: boolean;
  } | null {
    if (value === null || typeof value !== "object" || (value as { kind?: unknown }).kind !== "review")
      return null;
    const candidate = (value as { preview?: unknown }).preview;
    if (candidate === null || typeof candidate !== "object") return null;
    const preview = candidate as Record<string, unknown>;
    return {
      diff: Array.isArray(preview.diff)
        ? preview.diff.flatMap(item => {
            if (item === null || typeof item !== "object") return [];
            const entry = item as Record<string, unknown>;
            return typeof entry.path === "string" && typeof entry.change === "string"
              ? [{ path: entry.path, change: entry.change,
                  before: typeof entry.before === "string" ? entry.before : null,
                  after: typeof entry.after === "string" ? entry.after : null }]
              : [];
          })
        : [],
      acknowledgements: Array.isArray(preview.acknowledgements)
        ? preview.acknowledgements.flatMap(item => {
            if (item === null || typeof item !== "object") return [];
            const entry = item as Record<string, unknown>;
            return typeof entry.purposeId === "string" && typeof entry.impactId === "string"
              ? [{ purposeId: entry.purposeId, impactId: entry.impactId }]
              : [];
          })
        : [],
      changeCount: typeof preview.changeCount === "number" ? preview.changeCount : 0,
      previewChecksum: text(preview.previewChecksum),
      expiresAtUtc: text(preview.expiresAtUtc),
      agentId: text(preview.agentId),
      name: text(preview.name),
      configChecksum: text(preview.configChecksum),
      created: preview.created === true,
    };
  }

  function displayDiffValue(value: string | null): string {
    if (value === null) return "—";
    return value.length > 220 ? `${value.slice(0, 220)}…` : value;
  }
</script>

<main class="min-h-0 p-4 sm:p-6 lg:p-8">
  {#snippet deletionWorkflow()}
    <StudioNotice tone="danger"
      title={pageId === "session.detail" ? "Permanently delete this session" : "Permanently delete this thread"}>
      <p class="studio-text-safe">
        {pageId === "session.detail"
          ? "Deletion removes the selected session, its durable threads, and their journal-backed projections. Separately retained telemetry may remain as historical evidence."
          : "Deletion removes this durable thread and its transcript. A recursive deletion also removes every child thread in its subtree."}
      </p>
      <p class="mt-2 text-body-sm">Studio cannot undo this operation. The command is bound to the exact current resource authority and expires five minutes after review.</p>
      {#if pageId === "thread.detail"}
        <label class="mt-3 flex items-start gap-2 rounded-studio border border-studio-danger bg-studio-panel-soft p-3 text-body-sm">
          <input class="mt-1" type="checkbox" checked={deleteRecursive}
            onchange={event => setDeleteRecursive(event.currentTarget.checked)} disabled={deleteBusy}>
          <span><strong>Delete the complete child-thread subtree recursively.</strong><span class="mt-1 block text-studio-muted">Leave this disabled to let the host reject deletion when child threads still exist.</span></span>
        </label>
      {/if}
      <div class="mt-3 flex flex-wrap items-center gap-2">
        <StudioButton variant="danger" type="button" loading={deleteBusy}
          disabled={commandKind === "unresolved"} onclick={() => void reviewDeletion()}>
          Review deletion
        </StudioButton>
        <span class="text-body-sm text-studio-muted">Review does not delete anything. Multi-factor fresh authentication is required at execute time.</span>
      </div>
    </StudioNotice>

    {#if commandKind !== "closed"}
      <StudioPanel title="Reviewed destructive command"
        description="The command target, current authority, preview scope, and acknowledgement are checksum-bound.">
        <div class="flex flex-wrap gap-2">
          <StudioStatusBadge tone={commandKind === "review" ? "warning" : commandKind === "confirmed" || commandKind === "duplicate" ? "success" : commandKind === "failed" || commandKind === "freshAuthenticationUnsupported" ? "danger" : "info"}>
            {commandKind}
          </StudioStatusBadge>
          <StudioStatusBadge>{deletionCommandId() ?? "Deletion command"}</StudioStatusBadge>
          {#if deleteRecursive}<StudioStatusBadge tone="danger">Recursive subtree</StudioStatusBadge>{/if}
        </div>
        <dl class="grid gap-2 text-body-sm md:grid-cols-2">
          <div>
            <dt class="studio-label">Exact target</dt>
            <dd class="studio-text-safe break-all font-mono text-xs">
              {pageId === "session.detail" ? sessionProjection?.id : threadProjection?.id}
            </dd>
          </div>
          <div>
            <dt class="studio-label">Preview expires</dt>
            <dd>{commandPreview?.expiresAtUtc || "Awaiting review"}</dd>
          </div>
          {#if commandPreview?.previewChecksum}
            <div class="md:col-span-2">
              <dt class="studio-label">Preview checksum</dt>
              <dd class="break-all font-mono text-xs">{commandPreview.previewChecksum}</dd>
            </div>
          {/if}
        </dl>
        {#if commandKind === "review"}
          <label class="flex items-start gap-2 rounded-studio border border-studio-danger bg-studio-panel-soft p-3 text-body-sm">
            <input class="mt-1" type="checkbox" checked={deleteAccepted}
              onchange={event => acknowledgeDeletionImpact(event.currentTarget.checked)} disabled={deleteBusy}>
            <span><strong>I reviewed this exact target and understand that deletion is permanent.</strong><span class="mt-1 block text-studio-muted">The server will also require fresh multi-factor authentication before influence.</span></span>
          </label>
          <div class="flex flex-wrap gap-2">
            <StudioButton variant="danger" type="button" loading={deleteBusy} disabled={!deleteAccepted}
              onclick={() => void executeDeletion()}>Delete permanently</StudioButton>
            <StudioButton type="button" disabled={deleteBusy}
              onclick={() => { deleteAccepted = false; deleteFailure = ""; commands.close(); }}>
              Discard review
            </StudioButton>
          </div>
        {/if}
      </StudioPanel>
    {/if}
    {#if deleteFailure}
      <StudioNotice tone="danger" title="Deletion command did not confirm">
        <p class="studio-text-safe">{deleteFailure}</p>
      </StudioNotice>
    {/if}
  {/snippet}

  <div class="mx-auto grid w-full max-w-[96rem] gap-5">
    <StudioPageHeader eyebrow="HPD Agent Studio" title={title()}
      description="Runtime authority remains in the Agent host. Studio projects only the disclosed Agent, session, thread and capability evidence.">
      {#snippet actions()}
        {#if pageId === "agent.catalog"}
          <StudioButton variant="primary" type="button"
            onclick={() => location.assign("/studio/operations/agent-creation")}>+ New Agent</StudioButton>
        {/if}
        <StudioStatusBadge tone={phase === "ready" ? "success" : phase === "failed" ? "danger" : "warning"}>
          {phase === "ready" ? "Current" : phase === "failed" ? "Unavailable" : "Loading"}
        </StudioStatusBadge>
      {/snippet}
    </StudioPageHeader>

    {#if phase === "ready" && ["observability.overview", "observability.metrics", "observability.events", "trace.catalog", "trace.detail"].includes(pageId)}
      <nav class="flex flex-wrap gap-2" aria-label="Observability sections">
        <StudioButton size="small" variant={pageId === "observability.overview" ? "primary" : "secondary"}
          onclick={() => location.assign("/studio/operations/observability")}>Overview</StudioButton>
        <StudioButton size="small" variant={pageId === "trace.catalog" || pageId === "trace.detail" ? "primary" : "secondary"}
          onclick={() => location.assign("/studio/operations/observability/traces")}>Traces</StudioButton>
        <StudioButton size="small" variant={pageId === "observability.metrics" ? "primary" : "secondary"}
          onclick={() => location.assign("/studio/operations/observability/metrics")}>Metrics</StudioButton>
        <StudioButton size="small" variant={pageId === "observability.events" ? "primary" : "secondary"}
          onclick={() => location.assign("/studio/operations/observability/events")}>Events & logs</StudioButton>
      </nav>
      {#if observabilityViewSection}
        <section class="studio-panel p-4">
          <div class="flex flex-wrap items-end justify-between gap-3">
            <div class="grid min-w-[18rem] flex-1 gap-2 sm:max-w-xl">
              <label class="grid gap-1"><span class="studio-label">Saved view name</span><input class="studio-input" bind:value={savedViewName} maxlength="64" placeholder="Production failures" /></label>
              <div class="flex flex-wrap gap-2">
                <StudioButton type="button" size="small" onclick={saveCurrentView}>Save current scope</StudioButton>
                <StudioButton type="button" size="small" variant="secondary" onclick={exportCurrentProjection}>Export authorized JSON</StudioButton>
              </div>
            </div>
            <div class="grid min-w-[18rem] flex-1 gap-2 sm:max-w-xl">
              <p class="studio-label">Saved {observabilityViewSection} views</p>
              {#if savedViews.length > 0}
                <div class="grid gap-2">
                  {#each savedViews as view (view.id)}
                    <div class="flex flex-wrap items-center justify-between gap-2 rounded-studio border border-studio-line bg-studio-panel-soft px-3 py-2">
                      <button class="min-w-0 text-left" type="button" onclick={() => applySavedView(view)}><strong class="block truncate">{view.name}</strong><span class="text-meta text-studio-muted">{Object.entries(view.query).map(([key, value]) => `${key}=${value}`).join(" · ") || "No query filters"}</span></button>
                      <StudioButton type="button" size="small" variant="secondary" onclick={() => deleteSavedView(view.id)}>Delete</StudioButton>
                    </div>
                  {/each}
                </div>
              {:else}
                <p class="text-body-sm text-studio-muted">No saved views for this section. Saving stores the current URL scope on this device for this principal generation.</p>
              {/if}
            </div>
          </div>
          {#if savedViewMessage}<p class="mt-2 text-body-sm text-studio-muted">{savedViewMessage}</p>{/if}
        </section>
      {/if}
    {/if}

    {#if phase === "loading"}
      <StudioPanel title="Inspecting current authority…">
        <p class="text-sm text-studio-muted">Only server-authorized Agent evidence will be projected.</p>
      </StudioPanel>
    {:else if phase === "failed"}
      <StudioNotice tone="danger" title="Agent Studio observation failed">
        <p class="studio-text-safe">{failure}</p>
      </StudioNotice>
    {:else if pageId === "agent.catalog"}
      <section class="studio-panel overflow-x-auto">
        <table class="w-full border-collapse text-left text-sm">
          <thead><tr class="border-b border-studio-line"><th class="p-3">Agent</th><th class="p-3">ID</th><th class="p-3">Updated</th><th class="p-3"></th></tr></thead>
          <tbody>{#each rows(payload) as item}
            <tr class="border-b border-studio-line">
              <td class="p-3 font-semibold">{text(item.name)}</td>
              <td class="studio-text-safe p-3 font-mono text-xs">{text(item.id)}</td>
              <td class="p-3 text-studio-muted">{text(item.updatedAt)}</td>
              <td class="p-3 text-right"><a class="studio-button studio-button-sm" href={agentLink(item.resourceToken)}>Open</a></td>
            </tr>
          {/each}</tbody>
        </table>
        {#if rows(payload).length === 0}<p class="p-6 text-sm text-studio-muted">No Agent definitions are available.</p>{/if}
      </section>
    {:else if pageId === "agent.overview"}
      <section class="studio-panel grid gap-4 p-5">
        <div class="flex flex-wrap justify-between gap-3">
          <div><p class="studio-label">Agent definition</p><h2 class="mt-1 text-xl font-bold">{text((payload as Record<string, unknown>)?.name)}</h2></div>
          <span class="studio-badge">{text((payload as Record<string, unknown>)?.id)}</span>
        </div>
        <dl class="grid gap-2 text-sm md:grid-cols-2">
          <div><dt class="studio-label">Created</dt><dd>{text((payload as Record<string, unknown>)?.createdAt)}</dd></div>
          <div><dt class="studio-label">Updated</dt><dd>{text((payload as Record<string, unknown>)?.updatedAt)}</dd></div>
          <div><dt class="studio-label">ToolHarnesses</dt><dd>{Array.isArray((payload as Record<string, any>)?.config?.toolHarnesses) ? (payload as Record<string, any>).config.toolHarnesses.length : 0}</dd></div>
          <div><dt class="studio-label">Instructions</dt><dd class="truncate">{text((payload as Record<string, any>)?.config?.systemInstructions)}</dd></div>
        </dl>
      </section>
    {:else if pageId === "agent.workspace"}
      <section class="studio-panel flex flex-wrap items-start justify-between gap-4 p-5">
        <div class="min-w-0">
          <p class="studio-label">Experiment workspace</p>
          <h2 class="mt-1 text-heading">{text((payload as Record<string, unknown>)?.name) || agentId()}</h2>
          <p class="mt-1 text-body-sm text-studio-muted">Create a durable session, select a thread, submit input, and inspect the exact execution produced by the Agent host.</p>
        </div>
        <div class="flex flex-wrap gap-2">
          <StudioStatusBadge tone="info">{workspaceSessions.length} sessions</StudioStatusBadge>
          <StudioStatusBadge>{sessionThreads.length} threads</StudioStatusBadge>
          <StudioStatusBadge tone={activeExecution ? "warning" : "success"}>{activeExecution ? "Executing" : "Idle"}</StudioStatusBadge>
        </div>
      </section>

      {#if workspaceFailure}
        <StudioNotice tone="danger" title="Workspace action failed"><p class="studio-text-safe">{workspaceFailure}</p></StudioNotice>
      {/if}

      <div class="grid gap-4 md:grid-cols-[minmax(9rem,0.7fr)_minmax(11rem,0.9fr)_minmax(15rem,1.5fr)]">
        <StudioPanel title="Sessions" description="Only sessions containing this Agent's threads are disclosed.">
          <label class="grid gap-1 text-body-sm font-semibold">
            Current session
            <select class="studio-input" value={selectedSessionId} onchange={chooseSession} disabled={workspaceBusy}>
              {#each workspaceSessions as session (text(session.id))}
                <option value={text(session.id)}>{text(session.id)}</option>
              {/each}
            </select>
          </label>
          {#if selectedSession}
            <dl class="grid gap-2 text-body-sm">
              <div><dt class="studio-label">Last activity</dt><dd>{text(selectedSession.lastActivity) || "Unavailable"}</dd></div>
              <div><dt class="studio-label">Threads</dt><dd>{text(selectedSession.threadCount) || String(sessionThreads.length)}</dd></div>
            </dl>
          {:else}
            <p class="text-body-sm text-studio-muted">No Agent-scoped session exists yet.</p>
          {/if}
          <StudioButton variant="primary" type="button" loading={workspaceBusy}
            onclick={() => void createWorkspaceSession()}>New session</StudioButton>
        </StudioPanel>

        <StudioPanel title="Threads" description="Root and durable subagent threads remain in the selected session.">
          <div class="grid gap-2">
            {#each sessionThreads as thread (text(thread.id))}
              <button type="button" disabled={workspaceBusy}
                class={`w-full rounded-studio border p-3 text-left ${
                  text(thread.id) === selectedThreadId
                    ? "border-studio-focus bg-studio-panel-raised"
                    : "border-studio-line bg-studio-panel-soft"
                }`}
                onclick={() => void chooseThread(text(thread.id))}>
                <span class="flex flex-wrap items-center justify-between gap-2">
                  <strong class="truncate">{text(thread.name) || text(thread.id)}</strong>
                  <StudioStatusBadge tone={text(thread.kind) === "SubAgent" ? "warning" : "info"}>{text(thread.kind) || "Thread"}</StudioStatusBadge>
                </span>
                <span class="mt-1 block font-mono text-xs text-studio-muted">{text(thread.id)} · {text(thread.messageCount) || "0"} messages</span>
              </button>
            {:else}
              <p class="text-body-sm text-studio-muted">Select or create a session to inspect threads.</p>
            {/each}
          </div>
          {#if selectedSessionId}
            <label class="grid gap-1 text-body-sm font-semibold">
              New thread name
              <input class="studio-input" bind:value={newThreadName} maxlength="256"
                placeholder={`thread-${sessionThreads.length + 1}`} disabled={workspaceBusy}>
            </label>
            <StudioButton type="button" loading={workspaceBusy}
              onclick={() => void createWorkspaceThread()}>Create thread</StudioButton>
            {#if selectedThreadId}
              <details class="rounded-studio border border-studio-line bg-studio-panel-soft p-3">
                <summary class="cursor-pointer text-body-sm font-semibold">Fork selected thread</summary>
                <div class="mt-3 grid gap-3">
                  <label class="grid gap-1 text-body-sm font-semibold">
                    Fork point
                    <select class="studio-input" bind:value={forkFromMessageId} disabled={workspaceBusy}>
                      <option value="">Root · before any messages</option>
                      {#each transcriptMessages as message (message.id)}
                        <option value={message.id}>{messageOptionLabel(message)}</option>
                      {/each}
                    </select>
                  </label>
                  <label class="grid gap-1 text-body-sm font-semibold">
                    Fork name
                    <input class="studio-input" bind:value={forkName} maxlength="256"
                      placeholder={`${text(selectedThread?.name) || selectedThreadId}-fork`} disabled={workspaceBusy}>
                  </label>
                  <StudioButton type="button" loading={workspaceBusy}
                    onclick={() => void forkWorkspaceThread()}>Create fork</StudioButton>
                </div>
              </details>
            {/if}
          {/if}
        </StudioPanel>

        <div class="grid content-start gap-4">
          <StudioPanel title={selectedThread ? `${text(selectedThread.name)} conversation` : "Conversation"}
            description="Durable transcript messages and the current journal cursor are refreshed from the canonical thread history.">
            <div class="flex flex-wrap gap-2">
              <StudioStatusBadge tone={activityPhase === "live" ? "success" : activityPhase === "failed" ? "danger" : "warning"}>
                {activityPhase === "live" ? "Live" : activityPhase === "failed" ? "Live updates unavailable" : "Connecting"}
              </StudioStatusBadge>
              {#if threadActivity}
                <StudioStatusBadge>sequence {threadActivity.sequenceNumber}</StudioStatusBadge>
                <StudioStatusBadge tone={threadActivity.pendingRequestCount > 0 ? "warning" : "neutral"}>
                  {threadActivity.pendingRequestCount} pending requests
                </StudioStatusBadge>
              {/if}
            </div>
            {#if activityFailure}
              <StudioNotice tone="danger" title="Thread catch-up unavailable"><p class="studio-text-safe">{activityFailure}</p></StudioNotice>
            {/if}
            <div class="grid max-h-[30rem] gap-3 overflow-y-auto pr-1" aria-live="polite">
              {#each transcriptMessages as message (message.id)}
                {@const role = transcriptRole(message.role)}
                <article class={`rounded-studio border p-3 ${
                  role === "user"
                    ? "border-studio-focus bg-studio-panel-raised"
                    : role === "assistant"
                      ? "border-studio-line bg-studio-panel-soft"
                      : "border-studio-line bg-studio-canvas"
                }`}>
                  <div class="flex flex-wrap items-center justify-between gap-2">
                    <strong class="text-body-sm">{transcriptLabel(message)}</strong>
                    <span class="text-meta text-studio-muted">{shortTimestamp(message.createdAt)}</span>
                  </div>
                  <p class="studio-text-safe mt-2 whitespace-pre-wrap text-body">
                    {message.text || `${message.contentCount} non-text content item${message.contentCount === 1 ? "" : "s"}`}
                  </p>
                  <p class="mt-2 font-mono text-meta text-studio-muted">{message.id}</p>
                </article>
              {:else}
                <div class="rounded-studio border border-studio-line bg-studio-panel-soft p-4">
                  <p class="text-body-sm text-studio-muted">
                    {selectedThread ? "No messages are recorded in this thread yet. Submit input below to start the durable conversation." : "Select a thread to load its canonical transcript."}
                  </p>
                </div>
              {/each}
            </div>
            {#if recentActivityEvents.length > 0}
              <details class="rounded-studio border border-studio-line bg-studio-panel-soft p-3">
                <summary class="cursor-pointer text-body-sm font-semibold">Journal tail · {recentActivityEvents.length} events</summary>
                <div class="mt-3 grid max-h-56 gap-2 overflow-y-auto">
                  {#each recentActivityEvents.slice(-12).reverse() as event (event.id)}
                    <div class="grid gap-1 border-b border-studio-line pb-2 last:border-b-0 last:pb-0">
                      <div class="flex flex-wrap items-center justify-between gap-2">
                        <strong class="font-mono text-meta">{event.type}</strong>
                        <span class="text-meta text-studio-muted">#{event.sequenceNumber} · {shortTimestamp(event.occurredAt)}</span>
                      </div>
                      <p class="text-meta text-studio-muted">{event.kind} / {event.channel}{event.traceId ? ` · trace ${event.traceId}` : ""}</p>
                    </div>
                  {/each}
                </div>
              </details>
            {/if}
          </StudioPanel>

          {#if pendingRequests.length > 0}
            <StudioPanel title={`Pending requests · ${pendingRequests.length}`}
              description="Responses are bound to the exact durable request and current thread execution.">
              <div class="grid gap-4">
                {#each pendingRequests as request (request.requestId)}
                  {#if request.kind === "permission"}
                    <article class="grid gap-3 rounded-studio border border-studio-warning bg-studio-panel-soft p-4">
                      <div class="flex flex-wrap items-start justify-between gap-3">
                        <div class="min-w-0">
                          <p class="studio-label">Permission requested</p>
                          <h3 class="mt-1 text-heading">{request.functionName || request.authority || request.eventType}</h3>
                          {#if request.action}<p class="studio-text-safe mt-1 font-mono text-meta">{request.action}</p>{/if}
                        </div>
                        <StudioStatusBadge tone={request.risk === "Critical" || request.risk === "High" ? "danger" : request.risk === "Medium" ? "warning" : "info"}>
                          {request.risk || "Permission"}
                        </StudioStatusBadge>
                      </div>
                      {#if request.reason}<p class="studio-text-safe text-body-sm">{request.reason}</p>{/if}
                      {#if request.resource}<p class="studio-text-safe text-body-sm text-studio-muted">Resource: {request.resource}</p>{/if}
                      <label class="grid gap-1 text-body-sm font-semibold">
                        Response note
                        <input class="studio-input" value={permissionFeedback[request.requestId] ?? ""}
                          maxlength="8192" disabled={workspaceBusy}
                          oninput={event => permissionFeedback = {
                            ...permissionFeedback,
                            [request.requestId]: event.currentTarget.value
                          }}>
                      </label>
                      <div class="flex flex-wrap gap-2">
                        {#each request.choices as choice (choice.id)}
                          <StudioButton type="button" loading={workspaceBusy}
                            variant={choice.kind === "Deny" ? "danger" : choice.kind === "Allow" ? "primary" : "default"}
                            onclick={() => void respondToPermission(request, choice.id)}>{choice.label}</StudioButton>
                        {/each}
                      </div>
                    </article>
                  {:else if request.kind === "question"}
                    <article class="grid gap-4 rounded-studio border border-studio-info bg-studio-panel-soft p-4">
                      <div>
                        <p class="studio-label">Clarification requested</p>
                        <h3 class="mt-1 text-heading">{request.sourceName || request.eventType}</h3>
                      </div>
                      {#each request.questions as question (question.id)}
                        {@const answer = questionAnswer(request.requestId, question.id)}
                        <fieldset class="grid gap-3 rounded-studio border border-studio-line bg-studio-panel-raised p-3">
                          <legend class="px-1 text-body-sm font-semibold">{question.header || "Question"}</legend>
                          <p class="studio-text-safe whitespace-pre-wrap text-body">{question.text}</p>
                          {#if question.options.length > 0}
                            <div class="grid gap-2">
                              {#each question.options as option (option.id)}
                                <label class="flex items-start gap-2 rounded-studio border border-studio-line bg-studio-canvas p-2 text-body-sm">
                                  <input class="mt-1" type={question.multiple ? "checkbox" : "radio"}
                                    name={`${request.requestId}:${question.id}`}
                                    checked={answer.selected.includes(option.id)} disabled={workspaceBusy}
                                    onchange={event => toggleQuestionOption(request.requestId, question.id,
                                      option.id, question.multiple, event.currentTarget.checked)}>
                                  <span>
                                    <strong>{option.label}</strong>
                                    {#if option.description}<span class="studio-text-safe mt-1 block text-studio-muted">{option.description}</span>{/if}
                                  </span>
                                </label>
                              {/each}
                            </div>
                          {/if}
                          <label class="grid gap-1 text-body-sm font-semibold">
                            {question.options.length > 0 ? "Custom answer or clarification" : "Answer"}
                            <textarea class="studio-input min-h-20" value={answer.custom} maxlength="4096"
                              disabled={workspaceBusy}
                              oninput={event => setQuestionText(request.requestId, question.id, "custom",
                                event.currentTarget.value)}></textarea>
                          </label>
                          <label class="grid gap-1 text-body-sm font-semibold">
                            Notes
                            <input class="studio-input" value={answer.notes} maxlength="4096"
                              disabled={workspaceBusy}
                              oninput={event => setQuestionText(request.requestId, question.id, "notes",
                                event.currentTarget.value)}>
                          </label>
                        </fieldset>
                      {/each}
                      <div class="flex flex-wrap gap-2">
                        <StudioButton variant="primary" type="button" loading={workspaceBusy}
                          onclick={() => void respondToQuestion(request, "answered")}>Submit answers</StudioButton>
                        <StudioButton type="button" loading={workspaceBusy}
                          onclick={() => void respondToQuestion(request, "dismissed")}>Dismiss</StudioButton>
                        <StudioButton type="button" loading={workspaceBusy}
                          onclick={() => void respondToQuestion(request, "discuss")}>Discuss</StudioButton>
                      </div>
                    </article>
                  {:else if request.kind === "continuation"}
                    <article class="grid gap-3 rounded-studio border border-studio-warning bg-studio-panel-soft p-4">
                      <div>
                        <p class="studio-label">Continuation requested</p>
                        <h3 class="mt-1 text-heading">{request.sourceName || "Agent execution"}</h3>
                        <p class="studio-text-safe mt-1 text-body-sm text-studio-muted">
                          Iteration {request.currentIteration} of {request.maxIterations}. Continue with a bounded extension or stop the turn.
                        </p>
                      </div>
                      <label class="grid max-w-40 gap-1 text-body-sm font-semibold">
                        Extension iterations
                        <input class="studio-input" type="number" min="0" max="1000"
                          value={continuationExtensions[request.requestId] ?? 1} disabled={workspaceBusy}
                          oninput={event => continuationExtensions = {
                            ...continuationExtensions,
                            [request.requestId]: Math.max(0, Math.min(1000, event.currentTarget.valueAsNumber || 0))
                          }}>
                      </label>
                      <div class="flex flex-wrap gap-2">
                        <StudioButton variant="primary" type="button" loading={workspaceBusy}
                          onclick={() => void respondToContinuation(request, true)}>Approve continuation</StudioButton>
                        <StudioButton type="button" loading={workspaceBusy}
                          onclick={() => void respondToContinuation(request, false)}>Stop execution</StudioButton>
                      </div>
                    </article>
                  {:else if request.kind === "clientTool"}
                    <article class="grid gap-3 rounded-studio border border-studio-info bg-studio-panel-soft p-4">
                      <div class="flex flex-wrap items-start justify-between gap-3">
                        <div>
                          <p class="studio-label">Client tool requested</p>
                          <h3 class="mt-1 text-heading">{request.toolName || "Client capability"}</h3>
                          <p class="mt-1 font-mono text-meta text-studio-muted">{request.callId}</p>
                        </div>
                        <StudioStatusBadge tone="info">Client-owned execution</StudioStatusBadge>
                      </div>
                      {#if request.description}<p class="studio-text-safe text-body-sm">{request.description}</p>{/if}
                      <pre class="max-h-40 overflow-auto rounded-studio border border-studio-line bg-studio-canvas p-3 text-code studio-text-safe">{request.argumentsJson}</pre>
                      <label class="grid gap-1 text-body-sm font-semibold">
                        Completed result text
                        <textarea class="studio-input min-h-24" maxlength="8192"
                          value={clientToolContent[request.requestId] ?? ""} disabled={workspaceBusy}
                          oninput={event => clientToolContent = {
                            ...clientToolContent,
                            [request.requestId]: event.currentTarget.value
                          }}></textarea>
                      </label>
                      <label class="grid gap-1 text-body-sm font-semibold">
                        Rejection or failure message
                        <input class="studio-input" maxlength="8192"
                          value={clientToolErrors[request.requestId] ?? ""} disabled={workspaceBusy}
                          oninput={event => clientToolErrors = {
                            ...clientToolErrors,
                            [request.requestId]: event.currentTarget.value
                          }}>
                      </label>
                      <div class="flex flex-wrap gap-2">
                        <StudioButton variant="primary" type="button" loading={workspaceBusy}
                          disabled={!clientToolContent[request.requestId]?.trim()}
                          onclick={() => void respondToClientTool(request, "Completed")}>Complete</StudioButton>
                        <StudioButton type="button" loading={workspaceBusy}
                          disabled={!clientToolErrors[request.requestId]?.trim()}
                          onclick={() => void respondToClientTool(request, "Rejected")}>Reject</StudioButton>
                        <StudioButton variant="danger" type="button" loading={workspaceBusy}
                          disabled={!clientToolErrors[request.requestId]?.trim()}
                          onclick={() => void respondToClientTool(request, "Failed")}>Fail</StudioButton>
                      </div>
                      <p class="text-meta text-studio-muted">Accepted-background responses require client operation identity and remain a separate background-work handoff.</p>
                    </article>
                  {:else}
                    <article class="rounded-studio border border-studio-line bg-studio-panel-soft p-4">
                      <strong>{request.eventType || request.kind}</strong>
                      <p class="mt-1 text-body-sm text-studio-muted">This request type does not yet have a Studio response form.</p>
                    </article>
                  {/if}
                {/each}
              </div>
            </StudioPanel>
          {/if}

          <StudioPanel title="Submit input" description="Text is submitted through the same public hosted-runtime input contract used by the Agent API.">
            <label class="grid gap-1 text-body-sm font-semibold">
              Message
              <textarea class="studio-input min-h-40" bind:value={messageText} maxlength="32768"
                placeholder={selectedThread ? `Ask ${text(selectedThread.name)}…` : "Select a thread first…"}
                disabled={workspaceBusy || !selectedThread}></textarea>
            </label>
            <div class="flex flex-wrap items-center justify-between gap-3">
              <span class="text-body-sm text-studio-muted">
                {#if activeExecution}Steering guides the active execution at its next safe model boundary.{:else}The command creates one durable user turn.{/if}
              </span>
              {#if activeExecution}
                <StudioButton variant="primary" type="button" loading={workspaceBusy}
                  disabled={!selectedThread || !messageText.trim()}
                  onclick={() => void steerWorkspaceInput()}>Steer execution</StudioButton>
              {:else}
                <StudioButton variant="primary" type="button" loading={workspaceBusy}
                  disabled={!selectedThread || !messageText.trim()}
                  onclick={() => void submitWorkspaceInput()}>Run Agent</StudioButton>
              {/if}
            </div>
          </StudioPanel>

          <StudioPanel title={selectedThread ? `${text(selectedThread.name)} executions` : "Executions"}
            description="Execution evidence stays bound to this Agent, session, and thread.">
            <div class="grid max-h-80 gap-2 overflow-y-auto">
              {#each threadExecutions as item (text(item.id))}
                {@const execution = readExecution(item)[0]}
                {#if execution}
                  <article class="grid gap-2 rounded-studio border border-studio-line bg-studio-panel-soft p-3">
                    <div class="flex flex-wrap items-center justify-between gap-2">
                      <strong class="font-mono text-xs">{execution.id}</strong>
                      <StudioStatusBadge tone={execution.state.toLowerCase() === "completed" ? "success" : execution.state.toLowerCase() === "failed" ? "danger" : execution.state.toLowerCase() === "cancelled" ? "neutral" : "warning"}>{execution.state}</StudioStatusBadge>
                    </div>
                    <p class="text-body-sm text-studio-muted">{execution.startedAt || "Start unavailable"} → {execution.finishedAt || "In progress"}</p>
                    <StudioButton class="justify-self-start" size="small"
                      onclick={() => location.assign(studioLink("agent-executions", execution.resourceToken))}>Inspect execution</StudioButton>
                  </article>
                {:else}
                  <p class="text-body-sm text-studio-muted">This execution could not be projected.</p>
                {/if}
              {:else}
                <p class="text-body-sm text-studio-muted">No execution has been recorded for this thread.</p>
              {/each}
            </div>
            {#if activeExecution}
              <StudioButton variant="danger" type="button" loading={workspaceBusy}
                onclick={() => void cancelActiveExecution()}>Cancel active execution</StudioButton>
            {/if}
          </StudioPanel>
        </div>
      </div>
    {:else if pageId === "agent.configuration"}
      {#if configurationProjection}
        <StudioPanel title={`Configuration layers · ${configurationProjection.name || configurationProjection.agentId}`}
          description="Stored definition, host customization, and invocation overrides remain explicitly separate.">
          <div class="flex flex-wrap gap-2">
            <StudioStatusBadge tone={configurationProjection.preflightState === "valid" ? "success" : configurationProjection.preflightState === "invalid" ? "danger" : "warning"}>
              Preflight: {configurationProjection.preflightState}
            </StudioStatusBadge>
            <StudioStatusBadge>{configurationProjection.toolHarnesses.length} ToolHarnesses</StudioStatusBadge>
            <StudioStatusBadge>{configurationProjection.middlewareCount} middleware references</StudioStatusBadge>
          </div>
          <div class="grid gap-3 md:grid-cols-2">
            {#each configurationProjection.layers as layer (layer.id)}
              <article class="rounded-studio border border-studio-line bg-studio-panel-soft p-4">
                <div class="flex flex-wrap items-center justify-between gap-2">
                  <strong>{layer.label}</strong>
                  <StudioStatusBadge tone={layer.state === "authoritative" || layer.state === "configured" ? "info" : layer.state === "none" ? "neutral" : "warning"}>{layer.state}</StudioStatusBadge>
                </div>
                <p class="studio-text-safe mt-2 text-body-sm text-studio-muted">{layer.description}</p>
              </article>
            {/each}
          </div>
        </StudioPanel>
        <StudioPanel title="Effective stored values" description="Portable definition values currently resolved before host construction customization.">
          <dl class="grid gap-2 text-body-sm md:grid-cols-2 xl:grid-cols-4">
            <div><dt class="studio-label">Name</dt><dd>{configurationProjection.name || "Unavailable"}</dd></div>
            <div><dt class="studio-label">Max Agentic iterations</dt><dd>{configurationProjection.maxAgenticIterations}</dd></div>
            <div><dt class="studio-label">Max subagent depth</dt><dd>{configurationProjection.maxSubAgentDepth}</dd></div>
            <div><dt class="studio-label">Middleware references</dt><dd>{configurationProjection.middlewareCount}</dd></div>
          </dl>
          <div>
            <p class="studio-label">System instructions</p>
            <p class="studio-text-safe mt-1 whitespace-pre-wrap text-body">{configurationProjection.systemInstructions || "No system instructions are stored."}</p>
          </div>
        </StudioPanel>
        <StudioPanel title="ToolHarness references" description="Only portable references and generated configuration metadata are shown.">
          <div class="grid gap-2">
            {#each configurationProjection.toolHarnesses as reference (reference.name)}
              <article class="grid gap-2 rounded-studio border border-studio-line bg-studio-panel-soft p-3">
                <div class="flex flex-wrap items-center justify-between gap-2">
                  <strong class="font-mono">{reference.name}</strong>
                  <div class="flex flex-wrap gap-2">
                    <StudioStatusBadge tone={reference.hasConfiguration ? "info" : "neutral"}>{reference.hasConfiguration ? "typed config" : "default config"}</StudioStatusBadge>
                    <StudioStatusBadge tone={reference.hasMetadata ? "info" : "neutral"}>{reference.hasMetadata ? "metadata" : "default metadata"}</StudioStatusBadge>
                  </div>
                </div>
                <p class="text-body-sm text-studio-muted">
                  {reference.allFunctions ? "All generated functions selected" : `${reference.functions.length} selected functions`}
                  · {reference.middlewareConfigurationCount} middleware configs
                </p>
                {#if reference.functions.length > 0}
                  <p class="studio-text-safe font-mono text-xs text-studio-muted">{reference.functions.join(", ")}</p>
                {/if}
              </article>
            {:else}
              <p class="text-body-sm text-studio-muted">No ToolHarness reference is stored.</p>
            {/each}
          </div>
        </StudioPanel>
        <StudioPanel title="Configuration preflight" description="Stored-config validation and construction-critical declarations checked before any write command is enabled.">
          <div class="grid gap-2">
            {#each configurationProjection.checks as check (check.id)}
              <article class="flex flex-wrap items-start justify-between gap-3 rounded-studio border border-studio-line bg-studio-panel-soft p-3">
                <div class="min-w-0">
                  <strong>{check.label}</strong>
                  <p class="studio-text-safe mt-1 text-body-sm text-studio-muted">{check.summary}</p>
                </div>
                <StudioStatusBadge tone={check.state === "pass" ? "success" : check.state === "fail" ? "danger" : check.state === "warn" ? "warning" : "neutral"}>{check.state}</StudioStatusBadge>
              </article>
            {/each}
          </div>
          {#if configurationProjection.errors.length > 0}
            <StudioNotice tone="danger" title="Preflight errors">
              <ul class="grid gap-1">
                {#each configurationProjection.errors as error, index (index)}<li class="studio-text-safe">{error}</li>{/each}
              </ul>
            </StudioNotice>
          {/if}
        </StudioPanel>
        <StudioNotice tone="warning" title="Reviewed configuration updates">
          <p>A draft cannot be saved directly. Server preflight and a checksum-bound semantic review must both pass before the PATCH command is enabled.</p>
        </StudioNotice>
        <StudioPanel title="Draft preflight" description="Run the server's strict binding, AgentConfig validation, ToolHarness declaration, provider-composition, and run-override checks without persisting.">
          <label class="grid gap-1 text-body-sm font-semibold">
            Draft AgentConfig JSON
            <textarea class="studio-input min-h-[30rem] font-mono text-code" spellcheck="false"
              bind:value={draftJson}
              oninput={() => { draftPreflight = null; preflightFailure = ""; }}></textarea>
          </label>
          <div class="flex flex-wrap items-center gap-3">
            <StudioButton variant="primary" type="button" loading={preflightBusy}
              onclick={() => void runConfigurationPreflight()}>Run server preflight</StudioButton>
            <StudioButton type="button" loading={commandBusy}
              disabled={!draftPreflight || draftPreflight.state !== "valid"}
              onclick={() => void reviewConfigurationChange()}>Review configuration change</StudioButton>
            <span class="text-body-sm text-studio-muted">No configuration is saved by this action.</span>
          </div>
          {#if preflightFailure}
            <StudioNotice tone="danger" title="Draft preflight failed"><p class="studio-text-safe">{preflightFailure}</p></StudioNotice>
          {:else if draftPreflight}
            <div class="flex flex-wrap gap-2">
              <StudioStatusBadge tone={draftPreflight.state === "valid" ? "success" : "danger"}>{draftPreflight.state}</StudioStatusBadge>
              <StudioStatusBadge>{draftPreflight.toolHarnessCount} ToolHarnesses</StudioStatusBadge>
            </div>
            <div class="grid gap-2">
              {#each draftPreflight.checks as check (check.id)}
                <article class="flex flex-wrap items-start justify-between gap-3 rounded-studio border border-studio-line bg-studio-panel-soft p-3">
                  <div class="min-w-0"><strong>{check.label}</strong><p class="studio-text-safe mt-1 text-body-sm text-studio-muted">{check.summary}</p></div>
                  <StudioStatusBadge tone={check.state === "pass" ? "success" : check.state === "fail" ? "danger" : check.state === "warn" ? "warning" : "neutral"}>{check.state}</StudioStatusBadge>
                </article>
              {/each}
            </div>
            {#if draftPreflight.errors.length > 0}
              <StudioNotice tone="danger" title="Draft errors">
                <ul class="grid gap-1">
                  {#each draftPreflight.errors as error, index (index)}<li class="studio-text-safe">{error}</li>{/each}
                </ul>
              </StudioNotice>
            {/if}
          {/if}
          {#if commandFailure}
            <StudioNotice tone="danger" title="Configuration command failed">
              <p class="studio-text-safe">{commandFailure}</p>
            </StudioNotice>
          {/if}
        </StudioPanel>
        {#if commandKind !== "closed"}
          <StudioPanel title="Reviewed PATCH"
            description="This review is bound to the current Agent authority, current stored configuration, and the exact submitted draft.">
            <div class="flex flex-wrap gap-2">
              <StudioStatusBadge tone={commandKind === "review" ? "warning" : commandKind === "confirmed" || commandKind === "duplicate" ? "success" : commandKind === "failed" ? "danger" : "info"}>
                {commandKind}
              </StudioStatusBadge>
              {#if commandPreview}
                <StudioStatusBadge>{commandPreview.changeCount} semantic changes</StudioStatusBadge>
              {/if}
            </div>
            {#if commandKind === "previewing"}
              <p class="text-body-sm text-studio-muted">Computing the server-side semantic diff…</p>
            {:else if commandKind === "review" && commandPreview}
              <div class="overflow-x-auto rounded-studio border border-studio-line">
                <table class="w-full border-collapse text-left text-body-sm">
                  <thead>
                    <tr class="border-b border-studio-line bg-studio-panel-soft">
                      <th class="p-3">Field</th><th class="p-3">Change</th><th class="p-3">Stored</th><th class="p-3">Draft</th>
                    </tr>
                  </thead>
                  <tbody>
                    {#each commandPreview.diff as entry (`${entry.path}:${entry.change}`)}
                      <tr class="border-b border-studio-line last:border-b-0">
                        <td class="p-3 font-mono text-xs">{entry.path}</td>
                        <td class="p-3"><StudioStatusBadge tone={entry.change === "removed" ? "danger" : entry.change === "added" ? "success" : "warning"}>{entry.change}</StudioStatusBadge></td>
                        <td class="max-w-[24rem] break-words p-3 font-mono text-xs text-studio-muted">{displayDiffValue(entry.before)}</td>
                        <td class="max-w-[24rem] break-words p-3 font-mono text-xs">{displayDiffValue(entry.after)}</td>
                      </tr>
                    {/each}
                  </tbody>
                </table>
              </div>
              <label class="flex items-start gap-2 rounded-studio border border-studio-line bg-studio-panel-soft p-3 text-body-sm">
                <input class="mt-1" type="checkbox" checked={impactAccepted}
                  onchange={(event) => acknowledgeConfigurationImpact(event.currentTarget.checked)}>
                <span>I reviewed this exact diff. Supplied sections and collections replace the stored values; omitted PATCH fields are not merged from the draft editor.</span>
              </label>
              <div class="flex flex-wrap items-center gap-3">
                <StudioButton variant="primary" type="button" loading={commandBusy} disabled={!impactAccepted}
                  onclick={() => void executeConfigurationChange()}>Save reviewed configuration</StudioButton>
                <StudioButton type="button" disabled={commandBusy} onclick={() => { impactAccepted = false; commands.close(); }}>Discard review</StudioButton>
              </div>
            {:else if commandKind === "confirmed" || commandKind === "duplicate"}
              <StudioNotice tone="success" title="Configuration saved"><p>The reviewed PATCH completed and the page is refreshing from stored authority.</p></StudioNotice>
            {:else if commandKind === "failed" || commandKind === "retryable"}
              <StudioNotice tone="danger" title="Reviewed PATCH did not complete"><p class="studio-text-safe">Refresh the page and generate a new review before trying again.</p></StudioNotice>
            {:else if commandKind === "indeterminate" || commandKind === "unresolved"}
              <StudioNotice tone="warning" title="Update outcome is indeterminate">
                <p>Do not retry. Resolve the command receipt against the original request identity.</p>
              </StudioNotice>
              <StudioButton type="button" loading={commandBusy} onclick={() => void commands.resolve()}>Resolve receipt</StudioButton>
            {/if}
          </StudioPanel>
        {/if}
      {:else}
        <StudioNotice tone="danger" title="Configuration projection unavailable"><p>The response did not match the closed AgentConfig read model.</p></StudioNotice>
      {/if}
    {:else if pageId === "harness.catalog"}
      <div class="grid gap-3 lg:grid-cols-2">
        {#each rows(payload) as item}
          {@const projection = readToolHarness(item)}
          {#if projection}
            <StudioPanel>
              <div class="flex flex-wrap items-start justify-between gap-3">
                <div class="min-w-0">
                  <p class="studio-label">ToolHarness</p>
                  <h2 class="mt-1 text-heading">{projection.name}</h2>
                  <p class="studio-text-safe mt-1 text-body-sm text-studio-muted">{projection.description || "Capability composition and execution policy."}</p>
                  <p class="mt-2 font-mono text-meta text-studio-muted">{projection.ownerType || projection.id}</p>
                </div>
                <div class="flex flex-wrap justify-end gap-2">
                  <StudioStatusBadge tone={projection.availability === "available" ? "success" : projection.availability === "declared" ? "info" : "danger"}>{projection.availability}</StudioStatusBadge>
                  <StudioStatusBadge>{projection.functionCount} functions</StudioStatusBadge>
                  <StudioStatusBadge>{projection.actionCount} actions</StudioStatusBadge>
                </div>
              </div>
              <p class="text-body-sm text-studio-muted">{projection.agents.length} linked Agents · {projection.inheritedHarnessIdentities.length} inherited owners</p>
              <StudioButton class="justify-self-start" variant="primary"
                onclick={() => location.assign(studioLink("harnesses", projection.resourceToken))}>Inspect capability graph</StudioButton>
            </StudioPanel>
          {/if}
        {/each}
        {#if rows(payload).length === 0}<StudioPanel><p class="text-sm text-studio-muted">No ToolHarness configuration is disclosed.</p></StudioPanel>{/if}
      </div>
    {:else if pageId === "harness.detail"}
      {#if harness}
        <StudioPanel title={harness.name} description={harness.description || "ToolHarness capability composition."}>
          <div class="flex flex-wrap gap-2">
            <StudioStatusBadge tone={harness.availability === "available" ? "success" : harness.availability === "declared" ? "info" : "danger"}>{harness.availability}</StudioStatusBadge>
            <StudioStatusBadge tone="info">{harness.agents.length} linked Agents</StudioStatusBadge>
            <StudioStatusBadge>{harness.functionCount} AIFunctions</StudioStatusBadge>
            <StudioStatusBadge>{harness.actionCount} actions</StudioStatusBadge>
            <StudioStatusBadge>{harness.skills.length} skills</StudioStatusBadge>
            <StudioStatusBadge>{harness.subAgents.length} subagents</StudioStatusBadge>
          </div>
          <dl class="studio-disclosure-list">
            <div><dt>Activation identity</dt><dd class="font-mono text-xs">{harness.activationIdentity}</dd></div>
            <div><dt>Concrete owner</dt><dd class="font-mono text-xs">{harness.ownerType || "Unresolved"}</dd></div>
            <div><dt>Assembly</dt><dd class="font-mono text-xs">{harness.assembly || "Unavailable"}</dd></div>
            <div><dt>Instance</dt><dd>{harness.instanceStatus} · {harness.runtimeState}</dd></div>
            <div><dt>Inherited owners</dt><dd>{harness.inheritedHarnessIdentities.length > 0 ? harness.inheritedHarnessIdentities.join(", ") : "None"}</dd></div>
          </dl>
        </StudioPanel>

        {#if harness.diagnostics.length > 0}
          <StudioNotice tone="danger" title="ToolHarness diagnostics">
            <ul class="studio-disclosure-values">
              {#each harness.diagnostics as diagnostic}<li class="studio-text-safe">{diagnostic}</li>{/each}
            </ul>
          </StudioNotice>
        {/if}

        <StudioPanel>
          <CapabilityGraph projection={harness} />
        </StudioPanel>

        <div class="grid gap-4 xl:grid-cols-[minmax(0,1.35fr)_minmax(20rem,.65fr)]">
          <div class="grid content-start gap-4">
          {#each harness.functions as item (item.id)}
            <AIFunctionCard projection={item} />
          {:else}
            <StudioPanel title="No AIFunctions disclosed"><p class="text-sm text-studio-muted">The installed ToolHarness does not expose model-facing functions to this session.</p></StudioPanel>
          {/each}
          </div>

          <div class="grid content-start gap-4">
            <StudioPanel title="Construction contract" description="Generated settings, dependency resolution, metadata, and visibility declarations.">
              <div class="flex flex-wrap gap-2">
                <StudioStatusBadge tone={harness.settings.available ? "success" : "neutral"}>{harness.settings.available ? "Typed settings" : "No settings contract"}</StudioStatusBadge>
                <StudioStatusBadge>{harness.settings.supportsRunOverrides ? "Run overrides" : "Construction only"}</StudioStatusBadge>
                {#if harness.settings.hasValidator}<StudioStatusBadge>Validator</StudioStatusBadge>{/if}
              </div>
              {#if harness.settings.configurationType}<p class="font-mono text-xs studio-text-safe">{harness.settings.configurationType}</p>{/if}
              {#if harness.settings.schema}
                <p class="studio-label">Settings schema</p>
                <pre class="max-h-64 overflow-auto rounded-studio border border-studio-line bg-studio-panel-raised p-3 text-code studio-text-safe">{JSON.stringify(harness.settings.schema, null, 2)}</pre>
              {/if}
              {#if harness.settings.defaults}
                <p class="studio-label">Library defaults</p>
                <pre class="max-h-64 overflow-auto rounded-studio border border-studio-line bg-studio-panel-raised p-3 text-code studio-text-safe">{JSON.stringify(harness.settings.defaults, null, 2)}</pre>
              {/if}
              <p class="text-body-sm text-studio-muted">Metadata: {harness.metadata.typed ? harness.metadata.type : "No typed metadata contract"}</p>
            </StudioPanel>

            <StudioPanel title="Dependencies" description="Resolution is disclosed without returning dependency instances or secrets.">
              {#each harness.dependencies as dependency}
                <div class="border-b border-studio-line py-2 last:border-b-0">
                  <div class="flex flex-wrap items-center justify-between gap-2">
                    <span class="font-semibold">{dependency.name}</span>
                    <StudioStatusBadge tone={dependency.resolutionSource === "missing" ? "danger" : dependency.resolutionSource.includes("optional") ? "warning" : "success"}>{dependency.resolutionSource}</StudioStatusBadge>
                  </div>
                  <p class="font-mono text-xs text-studio-muted studio-text-safe">{dependency.typeName}</p>
                  <p class="text-meta text-studio-muted">{dependency.kind}{dependency.optional ? " · optional" : ""}</p>
                </div>
              {:else}
                <p class="text-body-sm text-studio-muted">No constructor dependency is declared.</p>
              {/each}
            </StudioPanel>

            <StudioPanel title="Middleware" description="Eager and expansion-gated execution middleware in declared order.">
              {#each harness.middleware as middleware}
                <div class="border-b border-studio-line py-2 last:border-b-0">
                  <div class="flex flex-wrap items-center justify-between gap-2">
                    <span class="font-semibold">{middleware.name}</span>
                    <StudioStatusBadge tone="info">{middleware.activation}</StudioStatusBadge>
                  </div>
                  <p class="font-mono text-xs text-studio-muted studio-text-safe">{middleware.type}</p>
                  <p class="text-meta text-studio-muted">{middleware.ownership} ownership{middleware.usesHarnessConfiguration ? " · harness settings" : ""}</p>
                </div>
              {:else}
                <p class="text-body-sm text-studio-muted">No ToolHarness middleware is declared.</p>
              {/each}
            </StudioPanel>

            <StudioPanel title="Visibility" description="Collapsing affects model visibility, not the persisted declaration.">
              <div class="flex flex-wrap gap-2">
                <StudioStatusBadge tone={harness.collapsed ? "warning" : "success"}>{harness.collapsed ? "Collapsed" : "Direct"}</StudioStatusBadge>
                <StudioStatusBadge>{harness.configuredFunctions.length} selected filters</StudioStatusBadge>
              </div>
              {#if harness.configuredFunctions.length > 0}<p class="font-mono text-xs studio-text-safe">{harness.configuredFunctions.join(", ")}</p>{/if}
              {#if harness.collapsingDescription}<p class="text-body-sm text-studio-muted">{harness.collapsingDescription}</p>{/if}
            </StudioPanel>
          </div>
        </div>
      {:else}
        <StudioNotice tone="danger" title="ToolHarness projection unavailable"><p>The response did not match the closed ToolHarness read model.</p></StudioNotice>
      {/if}
    {:else if pageId === "aiFunction.detail"}
      {#if functionProjection}
        <AIFunctionCard projection={functionProjection} expanded />
      {:else}
        <StudioNotice tone="danger" title="AIFunction projection unavailable"><p>The response did not match the closed AIFunction read model.</p></StudioNotice>
      {/if}
    {:else if pageId === "aiFunction.action.detail"}
      {#if actionProjection}
        <StudioPanel title={actionProjection.name} description={actionProjection.description || "Statically declared AIFunction action."}>
          <div class="flex flex-wrap gap-2">
            <StudioStatusBadge tone={permissionTone(actionProjection.permission.kind)}>Permission: {actionProjection.permission.kind}</StudioStatusBadge>
            <StudioStatusBadge>Invocation: {actionProjection.invocationMode}</StudioStatusBadge>
            <StudioStatusBadge>Handling: {actionProjection.invocationHandling}</StudioStatusBadge>
            <StudioStatusBadge>Replay: {actionProjection.replayPolicy}</StudioStatusBadge>
          </div>
          {#if actionProjection.permission.authority}<p class="text-body-sm text-studio-muted">Authority: <span class="font-mono studio-text-safe">{actionProjection.permission.authority}</span></p>{/if}
          {#if actionProjection.permission.policyId}<p class="text-body-sm text-studio-muted">Policy: <span class="font-mono studio-text-safe">{actionProjection.permission.policyId}</span></p>{/if}
          {#if actionProjection.permission.source}<p class="text-body-sm text-studio-muted">Declaration source: {actionProjection.permission.source}</p>{/if}
          {#if actionProjection.schema}
            <p class="studio-label mt-2">Closed action schema</p>
            <pre class="max-h-[28rem] overflow-auto rounded-studio border border-studio-line bg-studio-panel-raised p-4 text-code studio-text-safe">{JSON.stringify(actionProjection.schema, null, 2)}</pre>
          {/if}
        </StudioPanel>
      {:else}
        <StudioNotice tone="danger" title="AIFunctionAction projection unavailable"><p>The response did not match the closed action read model.</p></StudioNotice>
      {/if}
    {:else if pageId === "session.catalog"}
      <section class="studio-panel overflow-x-auto">
        <table class="w-full border-collapse text-left text-sm">
          <thead><tr class="border-b border-studio-line"><th class="p-3">Session</th><th class="p-3">Created</th><th class="p-3">Last activity</th><th class="p-3"></th></tr></thead>
          <tbody>{#each rows(payload) as item}
            <tr class="border-b border-studio-line"><td class="studio-text-safe p-3 font-mono text-xs">{text(item.id)}</td><td class="p-3">{text(item.createdAt)}</td><td class="p-3">{text(item.lastActivity)}</td><td class="p-3 text-right"><a class="studio-button studio-button-sm" href={`/studio/operations/sessions/${encodeURIComponent(text(item.resourceToken))}`}>Open</a></td></tr>
          {/each}</tbody>
        </table>
      </section>
    {:else if pageId === "session.detail"}
      {#if sessionProjection}
        <StudioPanel title={`Session ${sessionProjection.id}`} description={sessionProjection.description || "Durable Agent conversation scope."}>
          <div class="flex flex-wrap gap-2">
            <StudioStatusBadge tone="info">{sessionProjection.threads.length} threads</StudioStatusBadge>
            <StudioStatusBadge>{sessionProjection.threads.filter(item => item.kind === "SubAgent").length} subagent threads</StudioStatusBadge>
          </div>
          <dl class="grid gap-2 text-body-sm md:grid-cols-2">
            <div><dt class="studio-label">Created</dt><dd>{sessionProjection.createdAt || "Unavailable"}</dd></div>
            <div><dt class="studio-label">Last activity</dt><dd>{sessionProjection.lastActivity || "Unavailable"}</dd></div>
          </dl>
        </StudioPanel>
        <StudioPanel title="Thread hierarchy" description="Root and subagent work remain durable threads within the same session.">
          <div class="grid gap-2">
            {#each sessionProjection.threads as item (item.id)}
              <article class="grid gap-2 rounded-studio border border-studio-line bg-studio-panel-soft p-3 md:grid-cols-[minmax(0,1fr)_auto]">
                <div class="min-w-0">
                  <div class="flex flex-wrap items-center gap-2">
                    <strong>{item.name}</strong>
                    <StudioStatusBadge tone={item.kind === "SubAgent" ? "warning" : "info"}>{item.kind}</StudioStatusBadge>
                    <StudioStatusBadge>{item.state}</StudioStatusBadge>
                  </div>
                  <p class="mt-1 text-body-sm text-studio-muted">
                    Agent <span class="font-mono">{item.defaultAgentId || "Unknown"}</span> · {item.messageCount} messages
                    {#if item.parentThreadId} · Parent <span class="font-mono">{item.parentThreadId}</span>{/if}
                  </p>
                </div>
                <StudioButton class="justify-self-start" size="small" onclick={() => location.assign(studioLink("agent-threads", item.resourceToken))}>Inspect thread</StudioButton>
              </article>
            {:else}
              <p class="text-body-sm text-studio-muted">No threads are disclosed.</p>
            {/each}
          </div>
        </StudioPanel>
        {@render deletionWorkflow()}
      {:else}
        <StudioNotice tone="danger" title="Session projection unavailable"><p>The response did not match the closed Session read model.</p></StudioNotice>
      {/if}
    {:else if pageId === "thread.detail"}
      {#if threadProjection}
        <StudioPanel title={threadProjection.name} description="Durable thread transcript, ownership, and execution scope.">
          <div class="flex flex-wrap gap-2">
            <StudioStatusBadge tone={threadProjection.kind === "SubAgent" ? "warning" : "info"}>{threadProjection.kind}</StudioStatusBadge>
            <StudioStatusBadge>{threadProjection.state}</StudioStatusBadge>
            <StudioStatusBadge>{threadProjection.messageCount} messages</StudioStatusBadge>
            <StudioStatusBadge>{threadProjection.visibility}</StudioStatusBadge>
          </div>
          <dl class="grid gap-2 text-body-sm md:grid-cols-2">
            <div><dt class="studio-label">Session</dt><dd class="font-mono">{threadProjection.sessionId}</dd></div>
            <div><dt class="studio-label">Default Agent</dt><dd class="font-mono">{threadProjection.defaultAgentId}</dd></div>
            <div><dt class="studio-label">Parent thread</dt><dd class="font-mono">{threadProjection.parentThreadId ?? "Root thread"}</dd></div>
            <div><dt class="studio-label">Last activity</dt><dd>{threadProjection.lastActivity || "Unavailable"}</dd></div>
          </dl>
        </StudioPanel>
        {#if threadProjection.children.length > 0}
          <StudioPanel title="Subagent threads" description="Durable child threads grouped with the delegation invocation.">
            <div class="grid gap-2 md:grid-cols-2">
              {#each threadProjection.children as child (child.id)}
                <article class="grid gap-2 rounded-studio border border-studio-line bg-studio-panel-soft p-3">
                  <div class="flex flex-wrap items-center gap-2"><strong>{child.name}</strong><StudioStatusBadge tone="warning">{child.kind}</StudioStatusBadge></div>
                  <p class="text-body-sm text-studio-muted">{child.defaultAgentId} · {child.messageCount} messages · {child.state}</p>
                  <StudioButton class="justify-self-start" size="small" onclick={() => location.assign(studioLink("agent-threads", child.resourceToken))}>Inspect child thread</StudioButton>
                </article>
              {/each}
            </div>
          </StudioPanel>
        {/if}
        <StudioPanel title="Executions" description="Executions remain attached to this exact Agent, session, and thread.">
          <div class="grid gap-2">
            {#each threadProjection.executions as item (item.id)}
              <article class="grid gap-2 rounded-studio border border-studio-line bg-studio-panel-soft p-3 md:grid-cols-[minmax(0,1fr)_auto]">
                <div class="min-w-0">
                  <div class="flex flex-wrap items-center gap-2"><strong class="font-mono">{item.id}</strong><StudioStatusBadge tone={item.state === "Completed" ? "success" : item.state === "Failed" ? "danger" : "warning"}>{item.state}</StudioStatusBadge></div>
                  <p class="mt-1 text-body-sm text-studio-muted">{item.startedAt || "Start unavailable"} → {item.finishedAt || "Finish unavailable"} · {item.durationMs} ms</p>
                </div>
                <StudioButton class="justify-self-start" size="small" onclick={() => location.assign(studioLink("agent-executions", item.resourceToken))}>Inspect execution</StudioButton>
              </article>
            {:else}
              <p class="text-body-sm text-studio-muted">No execution is disclosed for this thread.</p>
            {/each}
          </div>
        </StudioPanel>
        {@render deletionWorkflow()}
      {:else}
        <StudioNotice tone="danger" title="Thread projection unavailable"><p>The response did not match the closed Thread read model.</p></StudioNotice>
      {/if}
    {:else if pageId === "execution.detail"}
      {#if executionProjection}
        <StudioPanel title={`Execution ${executionProjection.id}`} description="One admitted runtime execution with exact scope and selected capability evidence.">
          <div class="flex flex-wrap gap-2">
            <StudioStatusBadge tone={executionProjection.state === "Completed" ? "success" : executionProjection.state === "Failed" ? "danger" : "warning"}>{executionProjection.state}</StudioStatusBadge>
            <StudioStatusBadge tone={permissionTone(executionProjection.permission.kind)}>Permission: {executionProjection.permission.kind}</StudioStatusBadge>
            <StudioStatusBadge>{executionProjection.durationMs} ms</StudioStatusBadge>
          </div>
          <dl class="grid gap-3 text-body-sm md:grid-cols-2 xl:grid-cols-3">
            <div><dt class="studio-label">Agent</dt><dd class="font-mono">{executionProjection.defaultAgentId}</dd></div>
            <div><dt class="studio-label">Session</dt><dd class="font-mono">{executionProjection.sessionId}</dd></div>
            <div><dt class="studio-label">Thread</dt><dd class="font-mono">{executionProjection.threadId}</dd></div>
            <div><dt class="studio-label">ToolHarness</dt><dd class="font-mono">{executionProjection.selectedHarnessId || "Unavailable"}</dd></div>
            <div><dt class="studio-label">AIFunction</dt><dd class="font-mono">{executionProjection.selectedFunctionId || "Unavailable"}</dd></div>
            <div><dt class="studio-label">AIFunctionAction</dt><dd class="font-mono">{executionProjection.selectedActionId || "Unavailable"}</dd></div>
          </dl>
          <div class="rounded-studio border border-studio-line bg-studio-panel-soft p-4">
            <p class="studio-label">Result summary</p>
            <p class="studio-text-safe mt-1 text-body">{executionProjection.resultSummary || "No result summary is disclosed."}</p>
          </div>
        </StudioPanel>
      {:else}
        <StudioNotice tone="danger" title="Execution projection unavailable"><p>The response did not match the closed Execution read model.</p></StudioNotice>
      {/if}
    {:else if pageId === "trace.catalog"}
      <StudioPanel title="Trace query" description="Scope retained traces by Agent ownership, durable thread, outcome, and observed start time. Query state remains in the URL.">
        <form class="grid gap-3 lg:grid-cols-4" onsubmit={(event) => { event.preventDefault(); applyTraceFilters(); }}>
          <label class="grid gap-1"><span class="studio-label">Agent</span><input class="studio-input" bind:value={traceAgentIdFilter} placeholder="agent-id" /></label>
          <label class="grid gap-1"><span class="studio-label">Session</span><input class="studio-input" bind:value={traceSessionIdFilter} placeholder="session-id" /></label>
          <label class="grid gap-1"><span class="studio-label">Thread</span><input class="studio-input" bind:value={traceThreadIdFilter} placeholder="thread-id" /></label>
          <label class="grid gap-1"><span class="studio-label">Outcome</span>
            <select class="studio-input" bind:value={traceStateFilter}>
              <option value="">All outcomes</option>
              <option value="completed">Completed</option>
              <option value="failed">Failed</option>
              <option value="running">Running</option>
              <option value="cancelled">Cancelled</option>
            </select>
          </label>
          <label class="grid gap-1"><span class="studio-label">Started after</span><input class="studio-input" type="datetime-local" bind:value={traceAfterFilter} /></label>
          <label class="grid gap-1"><span class="studio-label">Started before</span><input class="studio-input" type="datetime-local" bind:value={traceBeforeFilter} /></label>
          <label class="grid gap-1"><span class="studio-label">Page size</span>
            <select class="studio-input" bind:value={traceLimitFilter}>
              <option value="25">25</option>
              <option value="50">50</option>
              <option value="100">100</option>
              <option value="200">200</option>
            </select>
          </label>
          <div class="flex items-end gap-2">
            <StudioButton type="submit">Apply filters</StudioButton>
            <StudioButton type="button" variant="secondary" onclick={resetTraceFilters}>Reset</StudioButton>
          </div>
        </form>
        <div class="mt-3 flex flex-wrap items-center gap-2">
          <StudioStatusBadge tone="info">{traceTotal} matching traces</StudioStatusBadge>
          <StudioStatusBadge>Offset {traceOffset}</StudioStatusBadge>
          {#if traceHasMore}<StudioStatusBadge tone="warning">More retained traces available</StudioStatusBadge>{/if}
        </div>
      </StudioPanel>
      <section class="studio-panel overflow-x-auto">
        <table class="w-full border-collapse text-left text-sm">
          <thead><tr class="border-b border-studio-line"><th class="p-3">Trace</th><th class="p-3">State</th><th class="p-3">Agent / session / thread</th><th class="p-3">Spans / events</th><th class="p-3">Duration</th><th class="p-3">Tokens</th><th class="p-3"></th></tr></thead>
          <tbody>{#each rows(payload) as item}
            {@const trace = readTrace(item)}
            {#if trace}<tr class="border-b border-studio-line">
              <td class="p-3"><strong>{trace.name}</strong><span class="mt-1 block font-mono text-xs text-studio-muted">{trace.id}</span></td>
              <td class="p-3"><StudioStatusBadge tone={trace.state.toLowerCase() === "completed" ? "success" : trace.state.toLowerCase() === "failed" ? "danger" : "warning"}>{trace.state}</StudioStatusBadge></td>
              <td class="studio-text-safe p-3 font-mono text-xs">{trace.agentId} / {trace.sessionId} / {trace.threadId}</td>
              <td class="p-3">{trace.spanCount} / {trace.eventCount}</td>
              <td class="p-3">{trace.durationMs} ms</td>
              <td class="p-3">{trace.totalTokens ?? "—"}</td>
              <td class="p-3 text-right"><StudioButton size="small" onclick={() => location.assign(studioLink("observability/traces", trace.resourceToken))}>Inspect trace</StudioButton></td>
            </tr>{/if}
          {:else}
            <tr><td colspan="7"><p class="p-6 text-sm text-studio-muted">No trace is disclosed.</p></td></tr>
          {/each}</tbody>
        </table>
      </section>
      <div class="flex flex-wrap items-center justify-between gap-2">
        <p class="text-body-sm text-studio-muted">Showing {rows(payload).length} trace{rows(payload).length === 1 ? "" : "s"} from offset {traceOffset}.</p>
        <div class="flex gap-2">
          <StudioButton type="button" variant="secondary" disabled={traceOffset === 0} onclick={() => pageTraces(-1)}>Previous</StudioButton>
          <StudioButton type="button" variant="secondary" disabled={!traceHasMore} onclick={() => pageTraces(1)}>Next</StudioButton>
        </div>
      </div>
    {:else if pageId === "trace.detail"}
      {#if traceProjection}
        <StudioPanel title={traceProjection.name} description="Trace scope, span hierarchy, timing, and authorized attributes.">
          <div class="flex flex-wrap gap-2">
            <StudioStatusBadge tone={traceProjection.state.toLowerCase() === "completed" ? "success" : traceProjection.state.toLowerCase() === "failed" ? "danger" : "warning"}>{traceProjection.state}</StudioStatusBadge>
            <StudioStatusBadge>{traceProjection.durationMs} ms</StudioStatusBadge>
            <StudioStatusBadge>{traceProjection.spans.length} spans</StudioStatusBadge>
            <StudioStatusBadge>{traceProjection.events.length} events</StudioStatusBadge>
            <StudioStatusBadge tone={traceProjection.spanSource === "otel-activity-projection" ? "info" : "neutral"}>{traceProjection.spanSource === "otel-activity-projection" ? "OTel Activity spans" : "Journal-derived spans"}</StudioStatusBadge>
            {#if traceProjection.totalTokens !== null}<StudioStatusBadge>{traceProjection.totalTokens} tokens</StudioStatusBadge>{/if}
          </div>
          {#if traceProjection.error}<StudioNotice tone="danger" title="Trace failed"><p class="studio-text-safe">{traceProjection.error}</p></StudioNotice>{/if}
          <dl class="grid gap-2 text-body-sm md:grid-cols-2 xl:grid-cols-4">
            <div><dt class="studio-label">Agent</dt><dd class="font-mono">{traceProjection.agentId || "Unavailable"}</dd></div>
            <div><dt class="studio-label">Session</dt><dd class="font-mono">{traceProjection.sessionId || "Unavailable"}</dd></div>
            <div><dt class="studio-label">Thread</dt><dd class="font-mono">{traceProjection.threadId || "Unavailable"}</dd></div>
            <div><dt class="studio-label">Execution</dt><dd class="font-mono">{traceProjection.executionId || "Unavailable"}</dd></div>
            <div><dt class="studio-label">Input tokens</dt><dd>{traceProjection.inputTokens ?? "Unavailable"}</dd></div>
            <div><dt class="studio-label">Output tokens</dt><dd>{traceProjection.outputTokens ?? "Unavailable"}</dd></div>
          </dl>
        </StudioPanel>
        <StudioPanel title="Spans" description="Spans are ordered by observed start time; parent identity provides the hierarchy.">
          <div class="grid gap-2">
            {#each traceProjection.spans as span (span.id)}
              <article class="rounded-studio border border-studio-line bg-studio-panel-soft p-3" style={`margin-inline-start:${span.parentSpanId ? 1.25 : 0}rem`}>
                <div class="flex flex-wrap items-center justify-between gap-2">
                  <div><p class="studio-label">{span.kind}</p><strong>{span.name}</strong><span class="ml-2 font-mono text-xs text-studio-muted">{span.id}</span></div>
                  <div class="flex items-center gap-2"><StudioStatusBadge tone={span.status.toLowerCase() === "completed" ? "success" : span.status.toLowerCase() === "failed" ? "danger" : "neutral"}>{span.status}</StudioStatusBadge><span class="text-body-sm">{span.durationMs} ms</span></div>
                </div>
                {#if Object.keys(span.attributes).length > 0}
                  <dl class="mt-2 grid gap-1 text-body-sm md:grid-cols-2">
                    {#each Object.entries(span.attributes) as [key, value]}
                      <div><dt class="studio-label">{key}</dt><dd class="studio-text-safe font-mono">{value}</dd></div>
                    {/each}
                  </dl>
                {/if}
                {#if span.spanEvents.length > 0}
                  <details class="mt-3 border-t border-studio-line pt-2">
                    <summary class="cursor-pointer text-body-sm font-semibold">Span events · {span.spanEvents.length}</summary>
                    <div class="mt-2 grid gap-2">
                      {#each span.spanEvents as spanEvent}
                        <div class="rounded-studio border border-studio-line p-2">
                          <p><strong>{spanEvent.name}</strong><span class="ml-2 text-meta text-studio-muted">{shortTimestamp(spanEvent.timestamp)}</span></p>
                          {#if Object.keys(spanEvent.attributes).length > 0}
                            <dl class="mt-1 grid gap-1 text-body-sm md:grid-cols-2">
                              {#each Object.entries(spanEvent.attributes) as [key, value]}
                                <div><dt class="studio-label">{key}</dt><dd class="studio-text-safe font-mono">{value}</dd></div>
                              {/each}
                            </dl>
                          {/if}
                        </div>
                      {/each}
                    </div>
                  </details>
                {/if}
              </article>
            {:else}
              <p class="text-body-sm text-studio-muted">No span is disclosed for this trace.</p>
            {/each}
          </div>
        </StudioPanel>
        <StudioPanel title="Canonical events" description="Ordered thread-journal facts correlated to this trace and span hierarchy.">
          <div class="max-h-[32rem] overflow-auto">
            <table class="w-full border-collapse text-left text-sm">
              <thead><tr class="border-b border-studio-line"><th class="p-3">Sequence</th><th class="p-3">Event</th><th class="p-3">Scope</th><th class="p-3">Span</th><th class="p-3">Occurred</th></tr></thead>
              <tbody>{#each traceProjection.events as event (event.id)}
                <tr class="border-b border-studio-line">
                  <td class="p-3 font-mono text-xs">{event.sequenceNumber}</td>
                  <td class="p-3"><strong>{event.type}</strong><span class="mt-1 block text-meta text-studio-muted">{event.kind} · {event.channel}</span></td>
                  <td class="studio-text-safe p-3 font-mono text-xs">{event.sessionId} / {event.threadId}</td>
                  <td class="studio-text-safe p-3 font-mono text-xs">{event.spanId || "—"}</td>
                  <td class="p-3 text-xs">{shortTimestamp(event.occurredAt)}</td>
                </tr>
              {:else}
                <tr><td colspan="5"><p class="p-4 text-sm text-studio-muted">No event is disclosed for this trace.</p></td></tr>
              {/each}</tbody>
            </table>
          </div>
        </StudioPanel>
      {:else}
        <StudioNotice tone="danger" title="Trace projection unavailable"><p>The response did not match the closed Trace read model.</p></StudioNotice>
      {/if}
    {:else if pageId === "observability.metrics"}
      {#if metricsProjection}
        <StudioPanel title="Metric scope" description="Apply the same Agent, durable thread, outcome, and observed-time scope used by Traces and Events.">
          <form class="grid gap-3 lg:grid-cols-4" onsubmit={(event) => { event.preventDefault(); applyMetricsFilters(); }}>
            <label class="grid gap-1"><span class="studio-label">Agent</span><input class="studio-input" bind:value={metricsAgentIdFilter} placeholder="agent-id" /></label>
            <label class="grid gap-1"><span class="studio-label">Session</span><input class="studio-input" bind:value={metricsSessionIdFilter} placeholder="session-id" /></label>
            <label class="grid gap-1"><span class="studio-label">Thread</span><input class="studio-input" bind:value={metricsThreadIdFilter} placeholder="thread-id" /></label>
            <label class="grid gap-1"><span class="studio-label">Outcome</span>
              <select class="studio-input" bind:value={metricsStateFilter}>
                <option value="">All outcomes</option>
                <option value="completed">Completed</option>
                <option value="failed">Failed</option>
                <option value="running">Running</option>
                <option value="cancelled">Cancelled</option>
              </select>
            </label>
            <label class="grid gap-1"><span class="studio-label">Started after</span><input class="studio-input" type="datetime-local" bind:value={metricsAfterFilter} /></label>
            <label class="grid gap-1"><span class="studio-label">Started before</span><input class="studio-input" type="datetime-local" bind:value={metricsBeforeFilter} /></label>
            <div class="flex items-end gap-2 lg:col-span-2">
              <StudioButton type="submit">Apply scope</StudioButton>
              <StudioButton type="button" variant="secondary" onclick={resetMetricsFilters}>Reset</StudioButton>
            </div>
          </form>
        </StudioPanel>
        <section class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {#each metricsProjection.measures as measure (measure.id)}
            <StudioPanel>
              <p class="studio-label">{measure.label}</p>
              <p class="mt-2 text-title">
                {measure.available && measure.value !== null
                  ? `${Number.isInteger(measure.value) ? measure.value : measure.value.toFixed(1)} ${measure.unit}`
                  : "Unavailable"}
              </p>
            </StudioPanel>
          {/each}
        </section>
        <div class="grid gap-4 xl:grid-cols-2">
          <StudioPanel title="Agent breakdown" description="Trace volume, failures, latency, and tokens remain scoped to their Agent identities.">
            <div class="overflow-auto">
              <table class="w-full border-collapse text-left text-sm">
                <thead><tr class="border-b border-studio-line"><th class="p-3">Agent</th><th class="p-3">Traces</th><th class="p-3">Failed</th><th class="p-3">Avg latency</th><th class="p-3">Tokens</th></tr></thead>
                <tbody>{#each metricsProjection.agents as agent (agent.id)}
                  <tr class="border-b border-studio-line"><td class="studio-text-safe p-3 font-mono text-xs">{agent.id || "Unknown"}</td><td class="p-3">{agent.traces}</td><td class="p-3">{agent.failed}</td><td class="p-3">{agent.averageDurationMs.toFixed(1)} ms</td><td class="p-3">{agent.totalTokens}</td></tr>
                {:else}
                  <tr><td colspan="5"><p class="p-4 text-sm text-studio-muted">No Agent metrics are available.</p></td></tr>
                {/each}</tbody>
              </table>
            </div>
          </StudioPanel>
          <StudioPanel title="Function calls" description="Call counts are derived from canonical tool-call-start events.">
            <div class="overflow-auto">
              <table class="w-full border-collapse text-left text-sm">
                <thead><tr class="border-b border-studio-line"><th class="p-3">ToolHarness</th><th class="p-3">AIFunction</th><th class="p-3">Calls</th></tr></thead>
                <tbody>{#each metricsProjection.functions as fn (`${fn.harness}:${fn.function}`)}
                  <tr class="border-b border-studio-line"><td class="studio-text-safe p-3 font-mono text-xs">{fn.harness || "Unattributed"}</td><td class="studio-text-safe p-3">{fn.function}</td><td class="p-3">{fn.calls}</td></tr>
                {:else}
                  <tr><td colspan="3"><p class="p-4 text-sm text-studio-muted">No function call is recorded.</p></td></tr>
                {/each}</tbody>
              </table>
            </div>
          </StudioPanel>
        </div>
        <StudioNotice tone="info" title="Metric capability boundary">
          <p>Time range: {metricsProjection.capabilities.timeRange}. Server filtering: {metricsProjection.capabilities.serverFiltering ? "available" : "not yet available"}. Payload inspection: {metricsProjection.capabilities.payloadInspection ? "available" : "not yet available"}.</p>
        </StudioNotice>
      {:else}
        <StudioNotice tone="danger" title="Metrics projection unavailable"><p>The response did not match the closed journal metrics read model.</p></StudioNotice>
      {/if}
    {:else if pageId === "observability.events"}
      {#if eventCatalog}
        <StudioPanel title="Canonical event journal" description="A bounded replay projection over the same durable events used for traces and observability.">
          <div class="flex flex-wrap items-center gap-2">
            <StudioStatusBadge tone="info">{eventTotal} matching events</StudioStatusBadge>
            <StudioStatusBadge>Offset {eventOffset}</StudioStatusBadge>
            <StudioStatusBadge tone={eventStreamPhase === "live" ? "success" : eventStreamPhase === "failed" ? "danger" : eventStreamPhase === "paused" ? "neutral" : "warning"}>
              {eventStreamPhase === "live" ? "Live tail" : eventStreamPhase === "failed" ? "Live tail unavailable" : eventStreamPhase === "paused" ? "Live tail paused" : "Connecting live tail"}
            </StudioStatusBadge>
            {#if liveEvents.length > 0}<StudioStatusBadge>{liveEvents.length} live events</StudioStatusBadge>{/if}
            <StudioStatusBadge>{eventCatalog.capabilities.payloadInspection ? "Payload inspection" : "Metadata only"}</StudioStatusBadge>
            {#if eventCatalog.truncated}<StudioStatusBadge tone="warning">Bounded result</StudioStatusBadge>{/if}
            <StudioButton type="button" size="small" variant="secondary" onclick={toggleEventTail}>{eventTailPaused ? "Resume live tail" : "Pause live tail"}</StudioButton>
          </div>
          {#if liveEventCursor}<p class="studio-text-safe mt-2 font-mono text-xs text-studio-muted">Live cursor {liveEventCursor}</p>{/if}
          {#if eventStreamFailure}<StudioNotice tone="danger" title="Live event observation failed"><p>{eventStreamFailure}</p></StudioNotice>{/if}
          <form class="grid gap-3 lg:grid-cols-4" onsubmit={(event) => { event.preventDefault(); applyEventFilters(); }}>
            <label class="grid gap-1"><span class="studio-label">Agent</span><input class="studio-input" bind:value={eventAgentIdFilter} placeholder="agent-id" /></label>
            <label class="grid gap-1"><span class="studio-label">Session</span><input class="studio-input" bind:value={eventSessionIdFilter} placeholder="session-id" /></label>
            <label class="grid gap-1"><span class="studio-label">Thread</span><input class="studio-input" bind:value={eventThreadIdFilter} placeholder="thread-id" /></label>
            <label class="grid gap-1"><span class="studio-label">Event type</span><input class="studio-input" bind:value={eventTypeFilter} placeholder="MessageTurnFinishedEvent" /></label>
            <label class="grid gap-1"><span class="studio-label">Trace</span><input class="studio-input" bind:value={eventTraceIdFilter} placeholder="trace-id" /></label>
            <label class="grid gap-1"><span class="studio-label">Span</span><input class="studio-input" bind:value={eventSpanIdFilter} placeholder="span-id" /></label>
            <label class="grid gap-1"><span class="studio-label">Occurred after</span><input class="studio-input" type="datetime-local" bind:value={eventAfterFilter} /></label>
            <label class="grid gap-1"><span class="studio-label">Occurred before</span><input class="studio-input" type="datetime-local" bind:value={eventBeforeFilter} /></label>
            <label class="grid gap-1"><span class="studio-label">Page size</span>
              <select class="studio-input" bind:value={eventLimitFilter}>
                <option value="50">50</option>
                <option value="100">100</option>
                <option value="250">250</option>
                <option value="500">500</option>
                <option value="1000">1000</option>
              </select>
            </label>
            <label class="flex items-end gap-2 pb-2 text-body-sm"><input type="checkbox" bind:checked={eventErrorsOnly} />Errors only</label>
            <div class="flex items-end gap-2 lg:col-span-2">
              <StudioButton type="submit">Apply filters</StudioButton>
              <StudioButton type="button" variant="secondary" onclick={resetEventFilters}>Reset</StudioButton>
            </div>
            <label class="grid gap-1"><span class="studio-label">Search this page</span><input class="studio-input" bind:value={eventSearch} placeholder="Attributes, source, correlation…" /></label>
          </form>
        </StudioPanel>
        <section class="studio-panel overflow-x-auto">
          <table class="w-full min-w-[78rem] border-collapse text-left text-sm">
            <thead><tr class="border-b border-studio-line"><th class="p-3">Sequence</th><th class="p-3">Event</th><th class="p-3">Source</th><th class="p-3">Agent / session / thread</th><th class="p-3">Trace / span</th><th class="p-3">Attributes</th><th class="p-3">Occurred</th><th class="p-3">Payload</th></tr></thead>
            <tbody>{#each filteredEvents as event (event.id)}
              <tr class="border-b border-studio-line">
                <td class="p-3 font-mono text-xs">{event.sequenceNumber}</td>
                <td class="p-3"><strong>{event.type}</strong><span class="mt-1 block text-meta text-studio-muted">{event.kind} · {event.channel}{event.isError ? " · error" : ""}</span></td>
                <td class="studio-text-safe p-3 text-xs">{event.source}</td>
                <td class="studio-text-safe p-3 font-mono text-xs">{event.agentId} / {event.sessionId} / {event.threadId}</td>
                <td class="studio-text-safe p-3 font-mono text-xs">{event.traceId || "—"} / {event.spanId || "—"}</td>
                <td class="studio-text-safe p-3 text-xs">{Object.entries(event.attributes).map(([key, value]) => `${key}=${value}`).join(" · ") || "—"}</td>
                <td class="p-3 text-xs">{shortTimestamp(event.occurredAt)}</td>
                <td class="whitespace-nowrap p-3 text-right"><StudioButton type="button" size="small" variant="secondary" loading={eventPayloadBusy && eventPayloadRequestId === event.id} onclick={() => void inspectEventPayload(event.id)}>Payload</StudioButton></td>
              </tr>
            {:else}
              <tr><td colspan="8"><p class="p-6 text-sm text-studio-muted">No event matches the current filter.</p></td></tr>
            {/each}</tbody>
          </table>
        </section>
        {#if eventPayloadFailure}
          <StudioNotice tone="danger" title="Payload unavailable"><p>{eventPayloadFailure}</p></StudioNotice>
        {:else if eventPayloadProjection}
          <StudioPanel title="Event payload" description={`${eventPayloadProjection.type} · ${eventPayloadProjection.format}${eventPayloadProjection.redacted ? " · sanitized" : ""}`}>
            {#if eventPayloadProjection.state === "authorized" && eventPayloadProjection.payload}
              <pre class="studio-text-safe max-h-[28rem] overflow-auto whitespace-pre-wrap rounded-studio border border-studio-line bg-studio-panel-soft p-3 font-mono text-xs">{eventPayloadProjection.payload}</pre>
            {:else if eventPayloadProjection.state === "denied"}
              <StudioNotice tone="warning" title="Payload access denied"><p>Your current principal may inspect event metadata but not its payload.</p></StudioNotice>
            {:else}
              <p class="text-body-sm text-studio-muted">This event does not expose a payload projection.</p>
            {/if}
          </StudioPanel>
        {/if}
        <div class="flex flex-wrap items-center justify-between gap-2">
          <p class="text-body-sm text-studio-muted">Showing {filteredEvents.length} event{filteredEvents.length === 1 ? "" : "s"} from offset {eventOffset}.</p>
          <div class="flex gap-2">
            <StudioButton type="button" variant="secondary" disabled={eventOffset === 0} onclick={() => pageEvents(-1)}>Previous</StudioButton>
            <StudioButton type="button" variant="secondary" disabled={!eventHasMore} onclick={() => pageEvents(1)}>Next</StudioButton>
          </div>
        </div>
      {:else}
        <StudioNotice tone="danger" title="Event projection unavailable"><p>The response did not match the closed journal-event read model.</p></StudioNotice>
      {/if}
    {:else if pageId === "observability.overview"}
      <section class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {#each [["Agents", (payload as Record<string, unknown>)?.agents], ["Sessions", (payload as Record<string, unknown>)?.sessions], ["Threads", (payload as Record<string, unknown>)?.threads], ["Traces", (payload as Record<string, unknown>)?.traces], ["Failed traces", (payload as Record<string, unknown>)?.failedTraces], ["Events", (payload as Record<string, unknown>)?.events], ["P95 latency", (payload as Record<string, unknown>)?.p95DurationMs === null ? "Unavailable" : `${(payload as Record<string, unknown>)?.p95DurationMs} ms`], ["Total tokens", (payload as Record<string, unknown>)?.totalTokens]] as [label, value]}
          <StudioPanel><p class="studio-label">{label}</p><p class="mt-2 text-title">{String(value ?? "—")}</p></StudioPanel>
        {/each}
      </section>
      <section class="studio-panel p-5">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <div><h2 class="text-heading">Evidence boundary</h2><p class="mt-1 text-sm text-studio-muted">Execution, event, token, and latency facts remain authoritative in the canonical thread journal. Correlated OpenTelemetry spans use a replaceable Studio telemetry projection and fall back to journal-derived spans when unavailable.</p></div>
          <div class="flex flex-wrap gap-2">
            <StudioButton type="button" onclick={() => location.assign("/studio/operations/observability/traces")}>Open traces</StudioButton>
            <StudioButton type="button" onclick={() => location.assign("/studio/operations/observability/metrics")}>Open metrics</StudioButton>
            <StudioButton type="button" onclick={() => location.assign("/studio/operations/observability/events")}>Open events</StudioButton>
          </div>
        </div>
      </section>
    {/if}

    <StudioButton class="justify-self-start" type="button" onclick={() => void load(new AbortController().signal)}>Refresh</StudioButton>
  </div>
</main>
