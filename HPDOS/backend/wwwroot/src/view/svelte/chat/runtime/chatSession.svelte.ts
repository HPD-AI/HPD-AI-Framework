import type {
  AgentClient,
  AgentEvent,
  BranchRun,
  BranchEvent,
  ChatSession,
  EventSubscription,
  RunConfig
} from "@hpd/hpd-agent-client";
import { EventTypes } from "@hpd/hpd-agent-client";
import { projectChatEvents } from "./chatProjector";
import type { ChatRuntimeEvent } from "./chatTypes";
import { chatErrorMessage } from "./errors";
import {
  buildWorkspaceInstructions,
  createSessionProviderModelMetadata,
  createRunWorkspace,
  type HpdosWorkspaceDescriptor
} from "./workspaceContext";

export type ChatSessionStateOptions = {
  client: AgentClient;
  agentId: string;
  sessionId: string;
  branchId?: string;
  workspace: HpdosWorkspaceDescriptor | null;
};

export class ChatSessionState {
  readonly client: AgentClient;
  readonly chat: ChatSession;
  readonly workspace: HpdosWorkspaceDescriptor | null;

  events = $state<ChatRuntimeEvent[]>([]);
  hydrated = $state(false);
  activeRun = $state<BranchRun | null>(null);
  submitting = $state(false);
  error = $state<string | null>(null);
  activeMessageTurn = $state(false);

  timeline = $derived(projectChatEvents(this.events));
  branchRunning = $derived(this.activeRun?.status === "active" || this.activeMessageTurn);

  #subscription: EventSubscription | null = null;

  constructor(options: ChatSessionStateOptions) {
    this.client = options.client;
    this.workspace = options.workspace;
    this.chat = options.client.chat.session({
      agentId: options.agentId,
      sessionId: options.sessionId,
      branchId: options.branchId ?? "main"
    });
  }

  async hydrate(): Promise<void> {
    this.hydrated = false;
    this.error = null;

    try {
      const [branchEvents, activeRun] = await Promise.all([
        this.chat.getBranchEvents(),
        this.chat.getActiveRun()
      ]);
      this.events = mergeHydratedChatEvents(branchEvents, this.events);
      this.activeRun = activeRun;
    } catch (error) {
      this.error = chatErrorMessage(error, "Failed to hydrate chat branch.");
      throw error;
    } finally {
      this.hydrated = true;
    }
  }

  attachLiveStream(): void {
    this.#subscription?.dispose();
    this.#subscription = this.client.onAny((event: AgentEvent) => {
      if (event.sessionId && event.sessionId !== this.chat.sessionId) return;
      if (event.branchId && event.branchId !== this.chat.branchId) return;
      this.append(event);
    });
    void this.chat.subscribeLive().catch((error) => {
      this.error = chatErrorMessage(error, "Failed to subscribe to live chat events.");
    });
  }

  detachLiveStream(): void {
    this.#subscription?.dispose();
    this.#subscription = null;
    void this.chat.disconnectLive();
  }

  append(event: AgentEvent | BranchEvent): void {
    if (event.type === EventTypes.BRANCH_RUN_STARTED) {
      const started = event as AgentEvent & {
        runtimeRunId: string;
        agentId: string;
        startedAt: string;
      };
      this.activeRun = {
        runtimeRunId: started.runtimeRunId,
        agentId: started.agentId,
        sessionId: event.sessionId ?? this.chat.sessionId,
        branchId: event.branchId ?? this.chat.branchId,
        status: "active",
        startedAt: started.startedAt,
        backgroundTasks: []
      };
    } else if (event.type === EventTypes.BRANCH_RUN_COMPLETED) {
      const completed = event as AgentEvent & {
        runtimeRunId: string;
        cancelled: boolean;
        errorType?: string | null;
        errorMessage?: string | null;
      };
      if (this.activeRun?.runtimeRunId === completed.runtimeRunId) {
        this.activeRun = {
          ...this.activeRun,
          status: completed.errorType ? "failed" : completed.cancelled ? "cancelled" : "completed",
          completedAt: event.timestamp ?? new Date().toISOString(),
          error: completed.errorType || completed.errorMessage
            ? { type: completed.errorType, message: completed.errorMessage }
            : null
        };
      }
    } else if (event.type === EventTypes.MESSAGE_TURN_STARTED) {
      this.activeMessageTurn = true;
    } else if (event.type === EventTypes.MESSAGE_TURN_FINISHED || event.type === EventTypes.MESSAGE_TURN_ERROR) {
      this.activeMessageTurn = false;
    }

    this.events = appendChatEvent(this.events, event);
  }

  async sendText(text: string, runConfig: RunConfig = {}): Promise<void> {
    const trimmed = text.trim();
    if (!trimmed) return;
    if (!this.workspace) {
      this.error = "Choose a workspace before using coding tools.";
      return;
    }
    if (this.branchRunning || this.submitting) {
      this.error = "This branch already has an active run.";
      return;
    }

    this.submitting = true;
    this.error = null;
    this.append(createLocalUserTextInputEvent({
      agentId: this.chat.agentId,
      sessionId: this.chat.sessionId,
      branchId: this.chat.branchId,
      text: trimmed,
      runConfig
    }));

    try {
      await this.chat.submitText(trimmed, {
        runConfig: mergeWorkspaceRunConfig(runConfig, this.workspace)
      });
      this.activeRun = await this.chat.getActiveRun();
      await this.persistProviderModel(runConfig);
    } catch (error) {
      this.error = chatErrorMessage(error, "Failed to send chat message.");
      throw error;
    } finally {
      this.submitting = false;
    }
  }

  dispose(): void {
    this.detachLiveStream();
    this.chat.dispose();
  }

  private async persistProviderModel(runConfig: RunConfig): Promise<void> {
    if (!runConfig.providerKey || !runConfig.modelId) return;

    await this.client.updateSession(this.chat.sessionId, {
      metadata: createSessionProviderModelMetadata({
        providerKey: runConfig.providerKey,
        modelId: runConfig.modelId
      })
    });
  }
}

export function mergeWorkspaceRunConfig(
  runConfig: RunConfig,
  workspace: HpdosWorkspaceDescriptor
): RunConfig {
  return {
    ...runConfig,
    additionalSystemInstructions: [
      runConfig.additionalSystemInstructions,
      buildWorkspaceInstructions(workspace)
    ].filter(Boolean).join("\n\n"),
    contextOverrides: {
      ...runConfig.contextOverrides,
      workspace: createRunWorkspace(workspace)
    }
  };
}

export function createLocalUserTextInputEvent(input: {
  agentId: string;
  sessionId: string;
  branchId: string;
  text: string;
  runConfig?: RunConfig;
}): AgentEvent {
  return {
    type: EventTypes.USER_TEXT_INPUT,
    eventId: `local:user-text:${localEventId()}`,
    agentId: input.agentId,
    sessionId: input.sessionId,
    branchId: input.branchId,
    text: input.text,
    runConfig: input.runConfig,
    timestamp: new Date().toISOString()
  };
}

function localEventId(): string {
  return globalThis.crypto?.randomUUID?.() ?? `${Date.now()}:${Math.random().toString(36).slice(2)}`;
}

export function mergeHydratedChatEvents(
  hydratedEvents: readonly ChatRuntimeEvent[],
  currentEvents: readonly ChatRuntimeEvent[]
): ChatRuntimeEvent[] {
  const merged = [...hydratedEvents];
  for (const event of currentEvents) {
    appendInto(merged, event);
  }

  return merged;
}

export function appendChatEvent(
  events: readonly ChatRuntimeEvent[],
  event: ChatRuntimeEvent
): ChatRuntimeEvent[] {
  const next = [...events];
  appendInto(next, event);
  return next;
}

function appendInto(events: ChatRuntimeEvent[], event: ChatRuntimeEvent): void {
  const key = chatEventKey(event);
  if (events.some((existing) => chatEventKey(existing) === key)) return;

  if (isLocalUserTextInput(event) && events.some((existing) => isDurableSameUserTextInput(existing, event))) {
    return;
  }

  if (isDurableUserTextInput(event)) {
    const duplicateLocalIndex = events.findIndex((existing) => isLocalSameUserTextInput(existing, event));
    if (duplicateLocalIndex >= 0) {
      events[duplicateLocalIndex] = event;
      return;
    }
  }

  events.push(event);
}

function chatEventKey(event: ChatRuntimeEvent): string {
  if (event.eventId) return `event:${event.eventId}`;
  if (event.sequenceNumber !== undefined) {
    return `sequence:${event.sessionId ?? ""}:${event.branchId ?? ""}:${event.sequenceNumber}`;
  }

  return `synthetic:${event.type}:${event.sessionId ?? ""}:${event.branchId ?? ""}:${JSON.stringify(event)}`;
}

function isLocalUserTextInput(event: ChatRuntimeEvent): boolean {
  return event.type === EventTypes.USER_TEXT_INPUT
    && typeof event.eventId === "string"
    && event.eventId.startsWith("local:user-text:");
}

function isDurableUserTextInput(event: ChatRuntimeEvent): boolean {
  return event.type === EventTypes.USER_TEXT_INPUT && !isLocalUserTextInput(event);
}

function isDurableSameUserTextInput(existing: ChatRuntimeEvent, local: ChatRuntimeEvent): boolean {
  return isDurableUserTextInput(existing) && sameUserTextInput(existing, local);
}

function isLocalSameUserTextInput(existing: ChatRuntimeEvent, durable: ChatRuntimeEvent): boolean {
  return isLocalUserTextInput(existing) && sameUserTextInput(existing, durable);
}

function sameUserTextInput(left: ChatRuntimeEvent, right: ChatRuntimeEvent): boolean {
  return left.type === EventTypes.USER_TEXT_INPUT
    && right.type === EventTypes.USER_TEXT_INPUT
    && textProp(left) === textProp(right)
    && scopeProp(left, "sessionId") === scopeProp(right, "sessionId")
    && scopeProp(left, "branchId") === scopeProp(right, "branchId");
}

function textProp(event: ChatRuntimeEvent): string | undefined {
  const record = event as unknown as Record<string, unknown>;
  return typeof record.text === "string" ? record.text : undefined;
}

function scopeProp(event: ChatRuntimeEvent, key: "sessionId" | "branchId"): string | undefined {
  const record = event as unknown as Record<string, unknown>;
  const value = record[key];
  return typeof value === "string" ? value : undefined;
}
