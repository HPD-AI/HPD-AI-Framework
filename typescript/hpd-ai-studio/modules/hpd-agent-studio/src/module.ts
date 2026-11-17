import {
  computeStudioFrontendAbiChecksum,
  defineStudioModuleDescriptor,
  studioCanonicalHash,
  studioClientId,
  studioPageId,
  studioSha256,
  validateStudioModuleActivation,
  type StudioFrameworkClientHostContext,
  type StudioModuleActivation,
  type StudioModuleActivationContext,
  type StudioPageComponent,
  type StudioPageComponentBinding,
  type StudioPageId,
  type StudioPageMounter
} from "@hpd-research/hpd-studio-core";
import { mount, unmount } from "svelte";
import AgentCreatePage from "./AgentCreatePage.svelte";
import AgentStudioPage from "./AgentStudioPage.svelte";
import { installAgentStudioRuntime, type AgentStudioClient } from "./runtime-context.ts";

interface StudioOperation {
  readonly operationId: string;
  readonly method: "GET" | "POST";
  readonly relativePathTemplate: string;
  readonly purpose: "observation" | "commandPreview";
  readonly requiredCapability: string;
  readonly maximumRequestBytes: number;
  readonly maximumResponseBytes: number;
  readonly deadlineMilliseconds: number;
  readonly requestMediaTypes: readonly string[];
  readonly responseMediaTypes: readonly string[];
  readonly requestHeaderNames: readonly string[];
  readonly responseHeaderNames: readonly string[];
}

const clientId = studioClientId("agent.control-plane");
const runtimeAbiChecksum = studioSha256("8c3a9949f994c287fa0b450c2013babe4069d4ba20b702a8543725115da05305");
const generatedContractChecksum = studioSha256("811e852537f0c02ad9807e187335b2f3ad6fdb153cdd4b224fc61e6159303e71");
const componentAbiChecksum = studioSha256("9b256fa00bf2b97c3d4ee8c50a89cb476c6ff3de30c8181eb001df867c1b09dd");

const operations: readonly StudioOperation[] = Object.freeze([
  operation("agent.configuration.get", "agents/{agentId}/configuration"),
  previewOperation("agent.configuration.preflight", "agents/{agentId}/configuration:preflight"),
  operation("agent.get", "agents/{agentId}"),
  operation("agent.list", "agents"),
  operation("agent.sessions.list", "agents/{agentId}/sessions"),
  operation("agent.thread.activity", "agents/{agentId}/sessions/{sessionId}/threads/{threadId}/activity"),
  operation("event.get", "events/{eventId}"),
  operation("events.list", "events"),
  operation("execution.get", "agents/{agentId}/sessions/{sessionId}/threads/{threadId}/executions/{executionId}"),
  operation("execution.list", "agents/{agentId}/sessions/{sessionId}/threads/{threadId}/executions"),
  operation("function.action.get", "harnesses/{harnessId}/functions/{functionId}/actions/{actionId}"),
  operation("function.get", "harnesses/{harnessId}/functions/{functionId}"),
  operation("harness.get", "harnesses/{harnessId}"),
  operation("harness.list", "harnesses"),
  operation("metrics.query", "observability/metrics"),
  operation("observability.summary", "observability/summary"),
  operation("session.get", "sessions/{sessionId}"),
  operation("session.list", "sessions"),
  operation("thread.get", "sessions/{sessionId}/threads/{threadId}"),
  operation("thread.list", "sessions/{sessionId}/threads"),
  operation("trace.get", "traces/{traceId}"),
  operation("trace.list", "traces")
]);

const operationInventoryChecksum = studioSha256(studioCanonicalHash("base.studio.framework-operation-inventory.v1", writer => {
  writer.string("agent.control-plane.v1");
  writer.count(operations.length);
  for (const value of operations) {
    writer.string(value.operationId);
    writer.discriminator(value.method === "GET" ? 1 : 2);
    writer.string(value.relativePathTemplate);
    writer.discriminator(value.purpose === "observation" ? 2 : 3);
    writer.string(value.requiredCapability);
    writer.int64(BigInt(value.maximumRequestBytes));
    writer.int64(BigInt(value.maximumResponseBytes));
    writer.int64(BigInt(value.deadlineMilliseconds));
    writer.count(value.requestMediaTypes.length);
    for (const item of value.requestMediaTypes) writer.string(item);
    writer.count(value.responseMediaTypes.length);
    for (const item of value.responseMediaTypes) writer.string(item);
    writer.count(value.requestHeaderNames.length);
    for (const item of value.requestHeaderNames) writer.string(item);
    writer.count(value.responseHeaderNames.length);
    for (const item of value.responseHeaderNames) writer.string(item);
  }
}));

const limitsChecksum = studioSha256(studioCanonicalHash("base.studio.frameworkClient.limits.v1", writer => {
  writer.int32(32);
  writer.int64(1_048_576n);
  writer.int64(8_388_608n);
  writer.int32(8);
  writer.int64(5_000n);
  writer.int64(30_000n);
  writer.int64(5_000n);
}));

const pageIds = [
  "agent.catalog",
  "agent.configuration",
  "agent.creation",
  "agent.overview",
  "agent.workspace",
  "aiFunction.action.detail",
  "aiFunction.detail",
  "execution.detail",
  "harness.catalog",
  "harness.detail",
  "observability.events",
  "observability.metrics",
  "observability.overview",
  "session.catalog",
  "session.detail",
  "thread.detail",
  "trace.catalog",
  "trace.detail"
] as const;

const pageComponents = Object.freeze(Object.fromEntries(pageIds.map(id => {
  const pageId = studioPageId(id);
  const component = id === "agent.creation" ? AgentCreatePage : AgentStudioPage;
  const binding: StudioPageComponentBinding = Object.freeze({
    componentExportId: `component.${id}`,
    componentAbiChecksum,
    component,
    mount: (context: Parameters<StudioPageMounter>[0]) => mountAgentPage(component, context)
  });
  return [pageId, binding] as const;
}))) as Readonly<Record<StudioPageId, StudioPageComponentBinding>>;

function mountAgentPage(component: StudioPageComponent,
  context: Parameters<StudioPageMounter>[0]): ReturnType<StudioPageMounter> {
  let dispose = render(context.props);
  return Object.freeze({
    update(props: Parameters<StudioPageMounter>[0]['props']): void {
      dispose();
      dispose = render(props);
    },
    destroy(): void {
      dispose();
      dispose = (): void => {};
    }
  });

  function render(props: Parameters<StudioPageMounter>[0]["props"]): () => void {
    const instance = mount(component, { target: context.target, props });
    return () => { void unmount(instance); };
  }
}

const clientSlots = Object.freeze([Object.freeze({
  clientId,
  version: 1,
  staticRuntimeAbiChecksum: runtimeAbiChecksum,
  protocol: "frameworkGeneratedContractV1" as const,
  generatedContractChecksum,
  operationInventoryChecksum,
  endpointSurfaceId: "agent.control-plane.v1",
  transportClass: "sameOriginShellAuthenticated" as const,
  owningPageIds: Object.freeze(pageIds.map(studioPageId)),
  limitsChecksum
})]);

const frontendAbiChecksum = computeStudioFrontendAbiChecksum("agents", 1, clientSlots, pageComponents);

/** Static, authorization-neutral Agent Studio frontend contribution. */
export const studioModuleDescriptor = defineStudioModuleDescriptor({
  moduleId: "agents",
  moduleVersion: 1,
  frontendAbiChecksum,
  clientSlots,
  pageComponents
});

/** Host-only generated-client activator consumed by the trusted Studio shell. */
export const studioFrameworkClientActivators = Object.freeze([Object.freeze({
  clientId,
  version: 1,
  runtimeAbiChecksum,
  generatedContractChecksum,
  operationInventoryChecksum,
  async create(context: StudioFrameworkClientHostContext): Promise<Readonly<{ readonly client: AgentStudioClient; dispose(): void }>> {
    const read = async (operation: string, path: string, signal = context.signal): Promise<unknown> => {
      const relativePathAndQuery = path.startsWith("/") ? path : `/${path}`;
      const response = await context.transport.execute({
        operation,
        method: "GET",
        relativePathAndQuery,
        headers: Object.freeze({}),
        body: undefined,
        maximumResponseBytes: context.limits.maximumResponseBytes,
        purpose: "observation",
        deadlineMilliseconds: context.limits.operationDeadlineMilliseconds,
        signal
      });
      if (!response.ok) throw new TypeError("Agent Studio observation is unavailable.");
      return response.json();
    };
    const client: AgentStudioClient = Object.freeze({
      principalGeneration: context.principalGeneration.toString(),
      listAgents: (signal?: AbortSignal) => read("agent.list", "agents", signal),
      getAgent: (agentId: string, signal?: AbortSignal) => read("agent.get", `agents/${encodeURIComponent(agentId)}`, signal),
      getAgentConfiguration: (agentId: string, signal?: AbortSignal) =>
        read("agent.configuration.get", `agents/${encodeURIComponent(agentId)}/configuration`, signal),
      preflightAgentConfiguration: (agentId: string, config: unknown, signal?: AbortSignal) =>
        preview("agent.configuration.preflight", `agents/${encodeURIComponent(agentId)}/configuration:preflight`,
          Object.freeze({ config }), signal),
      listAgentSessions: (agentId: string, signal?: AbortSignal) =>
        read("agent.sessions.list", `agents/${encodeURIComponent(agentId)}/sessions`, signal),
      getThreadActivity: (agentId: string, sessionId: string, threadId: string, signal?: AbortSignal) =>
        read("agent.thread.activity",
          `agents/${encodeURIComponent(agentId)}/sessions/${encodeURIComponent(sessionId)}/threads/${encodeURIComponent(threadId)}/activity`,
          signal),
      listSessions: (signal?: AbortSignal) => read("session.list", "sessions", signal),
      getSession: (sessionId: string, signal?: AbortSignal) => read("session.get", `sessions/${encodeURIComponent(sessionId)}`, signal),
      listThreads: (sessionId: string, signal?: AbortSignal) => read("thread.list", `sessions/${encodeURIComponent(sessionId)}/threads`, signal),
      getThread: (sessionId: string, threadId: string, signal?: AbortSignal) =>
        read("thread.get", `sessions/${encodeURIComponent(sessionId)}/threads/${encodeURIComponent(threadId)}`, signal),
      listExecutions: (agentId: string, sessionId: string, threadId: string, signal?: AbortSignal) =>
        read("execution.list", `agents/${encodeURIComponent(agentId)}/sessions/${encodeURIComponent(sessionId)}/threads/${encodeURIComponent(threadId)}/executions`, signal),
      getExecution: (agentId: string, sessionId: string, threadId: string, executionId: string, signal?: AbortSignal) =>
        read("execution.get", `agents/${encodeURIComponent(agentId)}/sessions/${encodeURIComponent(sessionId)}/threads/${encodeURIComponent(threadId)}/executions/${encodeURIComponent(executionId)}`, signal),
      getFunction: (harnessId: string, functionId: string, signal?: AbortSignal) =>
        read("function.get", `harnesses/${encodeURIComponent(harnessId)}/functions/${encodeURIComponent(functionId)}`, signal),
      getFunctionAction: (harnessId: string, functionId: string, actionId: string, signal?: AbortSignal) =>
        read("function.action.get", `harnesses/${encodeURIComponent(harnessId)}/functions/${encodeURIComponent(functionId)}/actions/${encodeURIComponent(actionId)}`, signal),
      listHarnesses: (signal?: AbortSignal) => read("harness.list", "harnesses", signal),
      getHarness: (harnessId: string, signal?: AbortSignal) => read("harness.get", `harnesses/${encodeURIComponent(harnessId)}`, signal),
      observabilitySummary: (signal?: AbortSignal) => read("observability.summary", "observability/summary", signal),
      queryMetrics: (query: Readonly<Record<string, string>> = {}, signal?: AbortSignal) =>
        read("metrics.query", withQuery("observability/metrics", query), signal),
      listEvents: (query: Readonly<Record<string, string>> = {}, signal?: AbortSignal) =>
        read("events.list", withQuery("events", query), signal),
      getEventPayload: (eventId: string, signal?: AbortSignal) =>
        read("event.get", `events/${encodeURIComponent(eventId)}`, signal),
      listTraces: (query: Readonly<Record<string, string>> = {}, signal?: AbortSignal) =>
        read("trace.list", withQuery("traces", query), signal),
      getTrace: (traceId: string, signal?: AbortSignal) => read("trace.get", `traces/${encodeURIComponent(traceId)}`, signal)
    });
    async function preview(operation: string, path: string, value: unknown, signal = context.signal): Promise<unknown> {
      const response = await context.transport.execute({
        operation,
        method: "POST",
        relativePathAndQuery: path.startsWith("/") ? path : `/${path}`,
        headers: Object.freeze({ "Content-Type": "application/json" }),
        body: JSON.stringify(value),
        maximumResponseBytes: context.limits.maximumResponseBytes,
        purpose: "commandPreview",
        deadlineMilliseconds: context.limits.operationDeadlineMilliseconds,
        signal
      });
      if (!response.ok) throw new TypeError("Agent Studio configuration preflight is unavailable.");
      return response.json();
    }

    function withQuery(path: string, query: Readonly<Record<string, string>>): string {
      const members = Object.entries(query)
        .filter(([, value]) => value.length > 0)
        .sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0)
        .map(([name, value]) => `${encodeURIComponent(name)}=${encodeURIComponent(value)}`);
      return members.length === 0 ? path : `${path}?${members.join("&")}`;
    }
    return Object.freeze({ client, dispose(): void {} });
  }
})]);

/** Activates one principal-generation-bound Agent Studio module instance. */
export async function activateStudioModule(context: StudioModuleActivationContext): Promise<StudioModuleActivation> {
  validateStudioModuleActivation(studioModuleDescriptor, context);
  const binding = context.clients.get(clientId);
  if (!binding || !isAgentClient(binding.client)) throw new TypeError("Agent Studio generated-client authority is unavailable.");
  const realtime = context.realtime;
  if (!realtime) throw new TypeError("Agent Studio realtime authority is unavailable.");
  const uninstall = installAgentStudioRuntime(Object.freeze({
    client: binding.client,
    applicationId: context.applicationId,
    principalGeneration: binding.client.principalGeneration,
    subscribeThreadActivity(target: unknown, sessionId: string, threadId: string, signal?: AbortSignal) {
      return realtime.subscribe("agent.studio.subscribe.thread.activity",
        Object.freeze({ target, sessionId, threadId }), signal);
    },
    subscribeEvents(signal?: AbortSignal) {
      return realtime.subscribe("agent.studio.subscribe.events", Object.freeze({}), signal);
    }
  }));
  let disposed = false;
  return Object.freeze({
    moduleId: "agents",
    moduleVersion: 1,
    frontendAbiChecksum,
    async dispose(): Promise<void> {
      if (disposed) return;
      disposed = true;
      uninstall();
    }
  });
}

function operation(operationId: string, relativePathTemplate: string): StudioOperation {
  return Object.freeze({
    operationId,
    method: "GET" as const,
    relativePathTemplate,
    purpose: "observation" as const,
    requiredCapability: "base.studio.resource.inspect",
    maximumRequestBytes: 0,
    maximumResponseBytes: 8_388_608,
    deadlineMilliseconds: 30_000,
    requestMediaTypes: Object.freeze([]),
    responseMediaTypes: Object.freeze(["application/json"]),
    requestHeaderNames: Object.freeze([]),
    responseHeaderNames: Object.freeze([])
  });
}

function previewOperation(operationId: string, relativePathTemplate: string): StudioOperation {
  return Object.freeze({
    operationId,
    method: "POST" as const,
    relativePathTemplate,
    purpose: "commandPreview" as const,
    requiredCapability: "base.studio.action.preview",
    maximumRequestBytes: 1_048_576,
    maximumResponseBytes: 8_388_608,
    deadlineMilliseconds: 30_000,
    requestMediaTypes: Object.freeze(["application/json"]),
    responseMediaTypes: Object.freeze(["application/json"]),
    requestHeaderNames: Object.freeze([]),
    responseHeaderNames: Object.freeze([])
  });
}

function isAgentClient(value: object): value is AgentStudioClient {
  const candidate = value as Record<string, unknown>;
  return ["listAgents", "getAgent", "getAgentConfiguration", "preflightAgentConfiguration", "listAgentSessions", "getThreadActivity", "listSessions", "getSession", "listThreads", "getThread", "listExecutions",
    "getExecution", "getFunction", "getFunctionAction", "listHarnesses", "getHarness", "observabilitySummary",
    "queryMetrics", "listEvents", "getEventPayload", "listTraces", "getTrace"]
    .every(name => typeof candidate[name] === "function");
}
