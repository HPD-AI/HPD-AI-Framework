import { describe, expect, test } from "bun:test";
import { EventTypes } from "@hpd/hpd-agent-client";
import { projectChatEvents } from "./runtime/chatProjector";
import { latestAgentPreview } from "./runtime/chatPreview";
import { groupTimelineTurns, groupWorkedItems, summarizeTurnDetails } from "./runtime/chatTurns";
import { renderMarkdown } from "./runtime/markdown";
import {
  appendChatEvent,
  createLocalUserTextInputEvent,
  mergeHydratedChatEvents,
  mergeWorkspaceRunConfig
} from "./runtime/chatSession.svelte";
import {
  buildModelPickerRows,
  createRunConfigForProviderModel,
  deleteHpdosProviderCredential,
  defaultProviderModelUiState,
  fetchHpdosModelCatalog,
  fetchHpdosProviderCatalog,
  fetchHpdosProviderDetail,
  fetchHpdosProviderStatus,
  hpdosModelCatalogEndpoint,
  hpdosProviderCatalogEndpoint,
  isProviderModelVisible,
  modelMatchesFilter,
  normalizeModelCatalog,
  normalizeProviderCatalog,
  normalizeProviderDetail,
  normalizeProviderModelUiState,
  normalizeProviderStatus,
  saveHpdosProviderCredential,
  selectProviderModel,
  setFavoriteProviderModel,
  setModelVisibility,
  setProviderOptionsJson,
  setProviderVisibility,
  visibleModelCatalog
} from "./runtime/providerModel";
import {
  buildWorkspaceInstructions,
  createRunWorkspace,
  createSessionMetadata,
  createSessionProviderModelMetadata,
  createSessionSearch,
  createUnscopedSessionSearch,
  isUnscopedSessionMetadata,
  readSessionProviderModel
} from "./runtime/workspaceContext";
import { chatErrorMessage } from "./runtime/errors";

describe("chat event projector", () => {
  test("projects locally submitted user text before branch hydration", () => {
    const event = createLocalUserTextInputEvent({
      agentId: "agent-1",
      sessionId: "session-1",
      branchId: "main",
      text: "hello from the local composer"
    });

    expect(projectChatEvents([event])).toEqual([
      {
        kind: "user-message",
        id: `user:${event.eventId}`,
        sourceEvents: [event.eventId],
        text: "hello from the local composer",
        messageId: undefined
      }
    ]);
  });

  test("keeps local user text when hydration races with a live send", () => {
    const local = createLocalUserTextInputEvent({
      agentId: "agent-1",
      sessionId: "session-1",
      branchId: "main",
      text: "hello from the local composer"
    });

    const hydrated = [
      { type: EventTypes.TEXT_MESSAGE_START, messageId: "m1", sessionId: "session-1", branchId: "main" },
      { type: EventTypes.TEXT_DELTA, messageId: "m1", sessionId: "session-1", branchId: "main", text: "hi" }
    ];

    const merged = mergeHydratedChatEvents(hydrated, [local]);
    expect(projectChatEvents(merged).map((item) => item.kind)).toEqual(["assistant-text", "user-message"]);
  });

  test("replaces local user text with durable branch user text when it appears", () => {
    const local = createLocalUserTextInputEvent({
      agentId: "agent-1",
      sessionId: "session-1",
      branchId: "main",
      text: "hello from the local composer"
    });
    const durable = {
      type: EventTypes.USER_TEXT_INPUT,
      eventId: "durable-user-event",
      sessionId: "session-1",
      branchId: "main",
      text: "hello from the local composer"
    };

    const events = appendChatEvent([local], durable);
    expect(events).toEqual([durable]);
    expect(projectChatEvents(events)[0]).toMatchObject({
      id: "user:durable-user-event",
      text: "hello from the local composer"
    });
  });

  test("projects hydrated text events into one assistant text item", () => {
    const items = projectChatEvents([
      { type: EventTypes.TEXT_MESSAGE_START, messageId: "m1", role: "assistant" },
      { type: EventTypes.TEXT_DELTA, messageId: "m1", text: "hello " },
      { type: EventTypes.TEXT_DELTA, messageId: "m1", text: "world" },
      { type: EventTypes.TEXT_MESSAGE_END, messageId: "m1" }
    ]);

    expect(items).toHaveLength(1);
    expect(items[0]).toMatchObject({
      kind: "assistant-text",
      messageId: "m1",
      text: "hello world",
      complete: true
    });
  });

  test("does not render empty text message envelopes as ghost rows", () => {
    const items = projectChatEvents([
      { type: EventTypes.TEXT_MESSAGE_START, messageId: "empty", role: "assistant" },
      { type: EventTypes.TEXT_MESSAGE_END, messageId: "empty" },
      { type: EventTypes.REASONING_MESSAGE_START, messageId: "r1" },
      { type: EventTypes.REASONING_DELTA, messageId: "r1", text: "checking" },
      { type: EventTypes.REASONING_MESSAGE_END, messageId: "r1" },
      { type: EventTypes.TOOL_CALL_START, callId: "c1", name: "ListDirectory" },
      { type: EventTypes.TOOL_CALL_END, callId: "c1" }
    ]);

    expect(items.map((item) => item.kind)).toEqual(["reasoning", "tool-call"]);
  });

  test("projects durable branch user text lifecycle as a user message", () => {
    const items = projectChatEvents([
      { type: "MESSAGE_STARTED", messageId: "u1", role: "user" },
      { type: EventTypes.TEXT_MESSAGE_START, messageId: "u1", role: "user" },
      { type: EventTypes.TEXT_DELTA, messageId: "u1", text: "who are you" },
      { type: EventTypes.TEXT_MESSAGE_END, messageId: "u1" },
      { type: "MESSAGE_COMPLETED", messageId: "u1" },
      { type: EventTypes.TEXT_MESSAGE_START, messageId: "a1", role: "assistant" },
      { type: EventTypes.TEXT_DELTA, messageId: "a1", text: "assistant answer" },
      { type: EventTypes.TEXT_MESSAGE_END, messageId: "a1" }
    ]);

    expect(items).toHaveLength(2);
    expect(items[0]).toMatchObject({
      kind: "user-message",
      messageId: "u1",
      text: "who are you"
    });
    expect(items[1]).toMatchObject({
      kind: "assistant-text",
      messageId: "a1",
      text: "assistant answer",
      complete: true
    });
  });

  test("projects reasoning separately from assistant output text", () => {
    const items = projectChatEvents([
      { type: EventTypes.REASONING_MESSAGE_START, messageId: "r1", role: "assistant" },
      { type: EventTypes.REASONING_DELTA, messageId: "r1", text: "thinking" },
      { type: EventTypes.REASONING_MESSAGE_END, messageId: "r1" }
    ]);

    expect(items).toHaveLength(1);
    expect(items[0]).toMatchObject({
      kind: "reasoning",
      messageId: "r1",
      text: "thinking",
      complete: true
    });
  });

  test("coalesces tool start args result and end into one tool item", () => {
    const items = projectChatEvents([
      { type: EventTypes.TOOL_CALL_START, callId: "c1", name: "read", messageId: "m1" },
      { type: EventTypes.TOOL_CALL_ARGS, callId: "c1", argsJson: "{\"path\":\"README.md\"}" },
      { type: EventTypes.TOOL_CALL_RESULT, callId: "c1", result: { text: "contents" } },
      { type: EventTypes.TOOL_CALL_END, callId: "c1" }
    ]);

    expect(items).toHaveLength(1);
    expect(items[0]).toMatchObject({
      kind: "tool-call",
      callId: "c1",
      name: "read",
      status: "completed",
      args: { path: "README.md" },
      result: { text: "contents" }
    });
  });

  test("projects command lifecycle events onto the correlated tool item", () => {
    const items = projectChatEvents([
      {
        type: "EXECUTE_COMMAND_PROCESS_STARTED",
        toolCallId: "cmd-1",
        functionName: "ExecuteCommand",
        commandId: "process-1",
        command: "bun test",
        baseCommand: "bun",
        category: "Test",
        workingDirectory: "/repo",
        shell: "zsh",
        processId: 42,
        timeoutMilliseconds: 30000,
        background: false,
        autoBackgroundEligible: true
      },
      {
        type: "EXECUTE_COMMAND_OUTPUT_CHUNK",
        toolCallId: "cmd-1",
        functionName: "ExecuteCommand",
        commandId: "process-1",
        command: "bun test",
        baseCommand: "bun",
        category: "Test",
        workingDirectory: "/repo",
        stream: "Stdout",
        text: "ok\n",
        observedAt: "2026-05-27T00:00:00Z",
        streamBytesObserved: 3,
        combinedBytesObserved: 3
      },
      {
        type: "EXECUTE_COMMAND_PROCESS_EXITED",
        toolCallId: "cmd-1",
        functionName: "ExecuteCommand",
        commandId: "process-1",
        command: "bun test",
        baseCommand: "bun",
        category: "Test",
        workingDirectory: "/repo",
        exitCode: 0,
        completionKind: "Exited",
        durationMilliseconds: 12,
        stdoutBytes: 3,
        stderrBytes: 0,
        combinedOutputBytes: 3,
        combinedBytesDiscarded: 0,
        outputTruncated: false
      }
    ]);

    expect(items).toHaveLength(1);
    expect(items[0]).toMatchObject({
      kind: "tool-call",
      callId: "cmd-1",
      name: "ExecuteCommand",
      status: "completed",
      command: {
        command: "bun test",
        shell: "zsh",
        liveOutput: "ok\n",
        exitCode: 0,
        durationMilliseconds: 12
      }
    });
  });

  test("projects durable file mutation events instead of parsing result text", () => {
    const items = projectChatEvents([
      {
        type: "FILE_EDIT_APPLIED",
        toolCallId: "edit-1",
        functionName: "EditFile",
        path: "/repo/a.ts",
        displayPath: "a.ts",
        mutationKind: "Edit",
        created: false,
        changed: true,
        before: { text: "old" },
        after: { text: "new" },
        textEdits: [],
        hunks: [{ oldStart: 1, oldLines: 1, newStart: 1, newLines: 1, lines: ["-old", "+new"] }],
        hunksTruncated: false,
        diffStat: { addedLines: 1, removedLines: 1 },
        editCount: 1,
        replacementCount: 1,
        replacements: [],
        normalizations: []
      }
    ]);

    expect(items).toHaveLength(1);
    expect(items[0]).toMatchObject({
      kind: "tool-call",
      callId: "edit-1",
      status: "completed",
      fileMutation: {
        type: "edit",
        displayPath: "a.ts",
        changed: true,
        diffStat: { addedLines: 1, removedLines: 1 }
      }
    });
  });

  test("keeps branch metadata, lifecycle, and unknown events out of the primary transcript", () => {
    const items = projectChatEvents([
      { type: "BRANCH_CREATED", branchId: "main" },
      { type: "MESSAGE_TURN_STARTED" },
      { type: "AGENT_TURN_STARTED" },
      { type: "STATE_SNAPSHOT" },
      { type: "ITERATION_START" },
      { type: "AGENT_DECISION" },
      { type: "SOMETHING_NEW", value: 1 }
    ]);

    expect(items).toEqual([]);
  });

  test("projects message turn errors as visible transcript errors", () => {
    const items = projectChatEvents([
      { type: EventTypes.MESSAGE_TURN_ERROR, message: "Provider rejected the request." }
    ]);

    expect(items).toHaveLength(1);
    expect(items[0]).toMatchObject({
      kind: "error",
      source: "Message turn",
      message: "Provider rejected the request."
    });
  });
});

describe("chat turn grouping", () => {
  test("collapses completed turn work before final assistant text", () => {
    const items = projectChatEvents([
      { type: EventTypes.USER_TEXT_INPUT, eventId: "u1", text: "read it" },
      { type: EventTypes.TEXT_MESSAGE_START, messageId: "a0", role: "assistant" },
      { type: EventTypes.TEXT_DELTA, messageId: "a0", text: "I will inspect it." },
      { type: EventTypes.TEXT_MESSAGE_END, messageId: "a0" },
      { type: EventTypes.REASONING_MESSAGE_START, messageId: "r1" },
      { type: EventTypes.REASONING_DELTA, messageId: "r1", text: "I should inspect files." },
      { type: EventTypes.REASONING_MESSAGE_END, messageId: "r1" },
      { type: EventTypes.TOOL_CALL_START, callId: "read-1", name: "ReadFile" },
      { type: EventTypes.TOOL_CALL_RESULT, callId: "read-1", result: { text: "file contents" } },
      { type: EventTypes.TEXT_MESSAGE_START, messageId: "a1", role: "assistant" },
      { type: EventTypes.TEXT_DELTA, messageId: "a1", text: "Done." },
      { type: EventTypes.TEXT_MESSAGE_END, messageId: "a1" }
    ]);

    const turns = groupTimelineTurns(items);

    expect(turns).toHaveLength(1);
    expect(turns[0]).toMatchObject({
      kind: "turn",
      complete: true,
      user: { kind: "user-message", text: "read it" },
      final: { kind: "assistant-text", text: "Done." },
      worked: [
        { kind: "assistant-text", text: "I will inspect it." },
        { kind: "reasoning" },
        { kind: "tool-call", name: "ReadFile" }
      ]
    });
    expect(turns[0].kind === "turn" ? summarizeTurnDetails(turns[0].worked) : "").toBe("1 message / 1 thought / 1 tool");
  });

  test("keeps active turn details visible until the final answer completes", () => {
    const items = projectChatEvents([
      { type: EventTypes.USER_TEXT_INPUT, eventId: "u1", text: "do it" },
      { type: EventTypes.REASONING_MESSAGE_START, messageId: "r1" },
      { type: EventTypes.REASONING_DELTA, messageId: "r1", text: "Working." },
      { type: EventTypes.TOOL_CALL_START, callId: "cmd-1", name: "ExecuteCommand" }
    ]);

    expect(groupTimelineTurns(items)[0]).toMatchObject({
      kind: "turn",
      complete: false,
      worked: [
        { kind: "reasoning", complete: false },
        { kind: "tool-call", status: "running" }
      ]
    });
  });

  test("keeps intermediary assistant text in order when more work follows it", () => {
    const items = projectChatEvents([
      { type: EventTypes.USER_TEXT_INPUT, eventId: "u1", text: "inspect it" },
      { type: EventTypes.TEXT_MESSAGE_START, messageId: "a0", role: "assistant" },
      { type: EventTypes.TEXT_DELTA, messageId: "a0", text: "Let me take a look." },
      { type: EventTypes.TEXT_MESSAGE_END, messageId: "a0" },
      { type: EventTypes.TOOL_CALL_START, callId: "list-1", name: "ListDirectory" },
      { type: EventTypes.TOOL_CALL_RESULT, callId: "list-1", result: { text: "files" } },
      { type: EventTypes.TOOL_CALL_END, callId: "list-1" },
      { type: EventTypes.REASONING_MESSAGE_START, messageId: "r1" },
      { type: EventTypes.REASONING_DELTA, messageId: "r1", text: "Need one more check." },
      { type: EventTypes.REASONING_MESSAGE_END, messageId: "r1" }
    ]);

    expect(groupTimelineTurns(items)[0]).toMatchObject({
      kind: "turn",
      complete: false,
      final: null,
      worked: [
        { kind: "assistant-text", text: "Let me take a look." },
        { kind: "tool-call", name: "ListDirectory" },
        { kind: "reasoning", text: "Need one more check." }
      ]
    });
  });

  test("compresses consecutive tool calls into a readable work summary", () => {
    const segments = groupWorkedItems([
      {
        kind: "tool-call",
        id: "tool:list-1",
        sourceEvents: [],
        callId: "list-1",
        name: "ListDirectory",
        status: "completed",
        rawEvents: []
      },
      {
        kind: "tool-call",
        id: "tool:list-2",
        sourceEvents: [],
        callId: "list-2",
        name: "ListDirectory",
        status: "completed",
        rawEvents: []
      },
      {
        kind: "tool-call",
        id: "tool:read-1",
        sourceEvents: [],
        callId: "read-1",
        name: "ReadFile",
        status: "completed",
        rawEvents: []
      },
      {
        kind: "tool-call",
        id: "tool:edit-1",
        sourceEvents: [],
        callId: "edit-1",
        name: "EditFile",
        status: "completed",
        fileMutation: {
          type: "edit",
          path: "/repo/a.ts",
          displayPath: "a.ts",
          created: false,
          changed: true
        },
        rawEvents: []
      }
    ]);

    expect(segments).toHaveLength(1);
    expect(segments[0]).toMatchObject({
      kind: "tool-group",
      summary: "explored 2 folders, read 1 file, edited 1 file"
    });
  });
});

describe("chat agent preview", () => {
  test("prefers the latest assistant output over tool activity in the current turn", () => {
    const items = projectChatEvents([
      { type: EventTypes.USER_TEXT_INPUT, eventId: "u1", text: "read it" },
      { type: EventTypes.TOOL_CALL_START, callId: "read-1", name: "ReadFile" },
      { type: EventTypes.TOOL_CALL_RESULT, callId: "read-1", result: { text: "file contents" } },
      { type: EventTypes.TEXT_MESSAGE_START, messageId: "a1", role: "assistant" },
      { type: EventTypes.TEXT_DELTA, messageId: "a1", text: "Here is the final answer." },
      { type: EventTypes.TEXT_MESSAGE_END, messageId: "a1" },
      { type: EventTypes.TOOL_CALL_END, callId: "read-1" }
    ]);

    expect(latestAgentPreview(items)).toEqual({
      turnId: "user:u1",
      label: "Agent",
      text: "Here is the final answer.",
      expandedText: "Here is the final answer.",
      complete: true
    });
  });

  test("does not reuse a previous turn preview before the current turn has output", () => {
    const items = projectChatEvents([
      { type: EventTypes.USER_TEXT_INPUT, eventId: "u1", text: "first" },
      { type: EventTypes.TEXT_MESSAGE_START, messageId: "a1", role: "assistant" },
      { type: EventTypes.TEXT_DELTA, messageId: "a1", text: "old answer" },
      { type: EventTypes.TEXT_MESSAGE_END, messageId: "a1" },
      { type: EventTypes.USER_TEXT_INPUT, eventId: "u2", text: "second" }
    ]);

    expect(latestAgentPreview(items)).toBeNull();
  });

  test("uses reasoning while the current turn has no assistant output yet", () => {
    const items = projectChatEvents([
      { type: EventTypes.USER_TEXT_INPUT, eventId: "u1", text: "inspect it" },
      { type: EventTypes.REASONING_MESSAGE_START, messageId: "r1" },
      { type: EventTypes.REASONING_DELTA, messageId: "r1", text: "checking files" }
    ]);

    expect(latestAgentPreview(items)).toEqual({
      turnId: "user:u1",
      label: "Thinking",
      text: "checking files",
      expandedText: "checking files",
      complete: false
    });
  });
});

describe("chat runtime errors", () => {
  test("rewrites browser URL pattern errors into actionable chat errors", () => {
    const message = chatErrorMessage(
      new Error("The string did not match the expected pattern."),
      "Failed to subscribe to live chat events."
    );

    expect(message).toBe(
      "Failed to subscribe to live chat events. The browser rejected an internal URL; check the HPD-Agent API base path and live connection route."
    );
  });
});

describe("chat markdown renderer", () => {
  test("renders common markdown for assistant output", async () => {
    const html = await renderMarkdown("## Plan\n\n- **Build** UI\n- Use `tests`");

    expect(html).toContain("<h2>Plan</h2>");
    expect(html).toContain("<li><strong>Build</strong> UI</li>");
    expect(html).toContain("<li>Use <code>tests</code></li>");
  });

  test("sanitizes raw html while preserving safe markdown links", async () => {
    const html = await renderMarkdown("Hello <script>x</script> [docs](https://example.com)");

    expect(html).not.toContain("<script>");
    expect(html).toContain('<a href="https://example.com" target="_blank" rel="noopener noreferrer">docs</a>');
  });

  test("renders fenced code with shiki highlighting", async () => {
    const html = await renderMarkdown("```js\nconst x = \"<tag>\";\n```");

    expect(html).toContain("shiki");
    expect(html).toContain("const");
    expect(html).toContain("&#x3C;tag>");
  });

  test("renders pipe tables", async () => {
    const html = await renderMarkdown("| Name | Status |\n| --- | --- |\n| **Chat** | `ready` |");

    expect(html).toContain("<table>");
    expect(html).toContain("<th>Name</th>");
    expect(html).toContain("<td><strong>Chat</strong></td>");
    expect(html).toContain("<td><code>ready</code></td>");
  });

  test("renders inline math", async () => {
    const html = await renderMarkdown("$x$");

    expect(html).toContain("katex");
    expect(html).toContain("<math");
  });
});

describe("workspace context", () => {
  const workspace = {
    id: "hpd-os",
    name: "HPD-OS",
    defaultRootId: "default",
    roots: [
      { id: "default", label: "HPD-OS", path: "/repo" },
      { id: "docs", label: "docs", path: "/docs" }
    ]
  };

  test("creates stable session metadata for workspace-scoped sessions", () => {
    expect(createSessionMetadata(workspace)).toEqual({
      app: "hpd-os",
      workspaceId: "hpd-os",
      defaultRootId: "default",
      defaultRootPath: "/repo",
      workspaceName: "HPD-OS",
      defaultRootLabel: "HPD-OS"
    });

    expect(createSessionSearch(workspace)).toMatchObject({
      metadata: {
        app: "hpd-os",
        workspaceId: "hpd-os"
      },
      limit: 50
    });
  });

  test("keeps provider model metadata on sessions without using it as a workspace search filter", () => {
    const providerModel = {
      providerKey: "openrouter",
      modelId: "anthropic/claude-sonnet"
    };

    expect(createSessionMetadata(workspace, providerModel)).toMatchObject({
      app: "hpd-os",
      workspaceId: "hpd-os",
      providerModel
    });

    expect(createSessionProviderModelMetadata(providerModel)).toEqual({ providerModel });
    expect(readSessionProviderModel({ providerModel })).toEqual(providerModel);
    expect(createSessionSearch(workspace).metadata).not.toHaveProperty("providerModel");
  });

  test("keeps unscoped sessions out of workspace session searches", () => {
    expect(createUnscopedSessionSearch(10)).toEqual({
      metadata: {
        app: "hpd-os"
      },
      limit: 10
    });

    expect(isUnscopedSessionMetadata({ app: "hpd-os" })).toBe(true);
    expect(isUnscopedSessionMetadata({ app: "hpd-os", workspaceId: "hpd-os" })).toBe(false);
  });

  test("creates harness workspace context separately from model instructions", () => {
    expect(createRunWorkspace(workspace)).toEqual({
      version: 1,
      defaultRootId: "default",
      roots: workspace.roots
    });

    expect(buildWorkspaceInstructions(workspace)).toContain("@docs => /docs");
  });

  test("preserves provider model selection while adding workspace run context", () => {
    const merged = mergeWorkspaceRunConfig(
      {
        providerKey: "anthropic",
        modelId: "claude-sonnet-4-5",
        providerOptionsJson: "{\"thinkingBudgetTokens\":4096}",
        additionalSystemInstructions: "Use concise status updates.",
        contextOverrides: {
          taskId: "task-1"
        }
      },
      workspace
    );

    expect(merged.providerKey).toBe("anthropic");
    expect(merged.modelId).toBe("claude-sonnet-4-5");
    expect(merged.providerOptionsJson).toBe("{\"thinkingBudgetTokens\":4096}");
    expect(merged.additionalSystemInstructions).toContain("Use concise status updates.");
    expect(merged.additionalSystemInstructions).toContain("@docs => /docs");
    expect(merged.contextOverrides.taskId).toBe("task-1");
    expect(merged.contextOverrides.workspace).toEqual(createRunWorkspace(workspace));
  });
});

describe("provider model state", () => {
  test("normalizes persisted state without preserving invalid or secret-shaped values", () => {
    const state = normalizeProviderModelUiState({
      selected: {
        providerKey: " anthropic ",
        modelId: " claude-sonnet "
      },
      recent: [
        { providerKey: "openai", modelId: "gpt-5" },
        { providerKey: "", modelId: "bad" },
        { providerKey: "openai", modelId: "gpt-5" }
      ],
      favorites: [{ providerKey: "anthropic", modelId: "claude-sonnet" }],
      visibility: { "openai:gpt-5": "hidden", bad: "nope" },
      providerVisibility: { ollama: "visible", bad: "nope" },
      providerOptionsJson: { anthropic: " {\"thinkingBudgetTokens\":4096} " },
      apiKey: "should-not-be-kept"
    });

    expect(state).toEqual({
      selected: {
        providerKey: "anthropic",
        modelId: "claude-sonnet"
      },
      recent: [{ providerKey: "openai", modelId: "gpt-5" }],
      favorites: [{ providerKey: "anthropic", modelId: "claude-sonnet" }],
      visibility: { "openai:gpt-5": "hidden" },
      providerVisibility: { ollama: "visible" },
      providerOptionsJson: { anthropic: "{\"thinkingBudgetTokens\":4096}" }
    });
  });

  test("selecting a model records selection and de-duplicated recents", () => {
    let state = defaultProviderModelUiState();
    state = selectProviderModel(state, { providerKey: "openai", modelId: "gpt-5" });
    state = selectProviderModel(state, {
      providerKey: "anthropic",
      modelId: "claude-sonnet"
    });
    state = selectProviderModel(state, { providerKey: "openai", modelId: "gpt-5" });

    expect(state.selected).toEqual({ providerKey: "openai", modelId: "gpt-5" });
    expect(state.recent).toEqual([
      { providerKey: "openai", modelId: "gpt-5" },
      { providerKey: "anthropic", modelId: "claude-sonnet" }
    ]);
  });

  test("visibility rules keep explicit hidden items out and useful models in", () => {
    let state = defaultProviderModelUiState();
    state = setFavoriteProviderModel(state, { providerKey: "anthropic", modelId: "claude-opus" }, true);
    state = selectProviderModel(state, { providerKey: "openai", modelId: "gpt-5" });
    state = setModelVisibility(state, { providerKey: "openai", modelId: "gpt-5" }, "hidden");
    state = setProviderVisibility(state, "ollama", "hidden");

    expect(isProviderModelVisible(state, {
      providerKey: "anthropic",
      modelId: "claude-opus",
      status: "deprecated"
    })).toBe(true);

    expect(isProviderModelVisible(state, {
      providerKey: "openai",
      modelId: "gpt-5",
      status: "active"
    })).toBe(false);

    expect(isProviderModelVisible(state, {
      providerKey: "ollama",
      modelId: "local-model",
      recommended: true
    })).toBe(false);

    expect(isProviderModelVisible(state, {
      providerKey: "google-ai",
      modelId: "alpha-model",
      status: "alpha"
    })).toBe(false);
  });

  test("provider model selection composes into run config without provider branching", () => {
    expect(createRunConfigForProviderModel(
      {
        providerKey: "openrouter",
        modelId: "anthropic/claude-sonnet"
      },
      {
        openrouter: "{\"appName\":\"HPD-OS\"}"
      },
      {
        additionalSystemInstructions: "Keep output concise.",
        contextOverrides: { taskId: "task-1" }
      }
    )).toEqual({
      providerKey: "openrouter",
      modelId: "anthropic/claude-sonnet",
      providerOptionsJson: "{\"appName\":\"HPD-OS\"}",
      additionalSystemInstructions: "Keep output concise.",
      contextOverrides: { taskId: "task-1" }
    });
  });

  test("provider options json stays attached to provider and can be cleared", () => {
    let state = selectProviderModel(defaultProviderModelUiState(), {
      providerKey: "openrouter",
      modelId: "anthropic/claude-sonnet"
    });

    state = setProviderOptionsJson(state, "openrouter", " {\"appName\":\"HPD-OS\"} ");
    expect(state.selected).toEqual({
      providerKey: "openrouter",
      modelId: "anthropic/claude-sonnet"
    });
    expect(state.providerOptionsJson).toEqual({ openrouter: "{\"appName\":\"HPD-OS\"}" });
    expect(createRunConfigForProviderModel(state.selected, state.providerOptionsJson)).toMatchObject({
      providerOptionsJson: "{\"appName\":\"HPD-OS\"}"
    });

    state = setProviderOptionsJson(state, "openrouter", null);
    expect(state.providerOptionsJson).toEqual({});
  });

  test("normalizes provider catalog from HPD-OS without preserving malformed rows", () => {
    expect(normalizeProviderCatalog([
      {
        providerKey: " anthropic ",
        displayName: " Anthropic ",
        documentationUrl: " https://docs.anthropic.com ",
        capabilities: {
          streaming: true,
          toolCalling: true,
          vision: false,
          audio: false
        },
        auth: {
          kind: " apiKey ",
          required: true,
          sources: [" environment ", "", "runtime"]
        },
        configurationFields: [
          {
            key: "baseUrl",
            label: "Base URL",
            kind: "url",
            required: false,
            description: "Optional endpoint"
          },
          { key: "", label: "Bad", kind: "text" }
        ]
      },
      { providerKey: "", displayName: "bad" }
    ])).toEqual([
      {
        providerKey: "anthropic",
        displayName: "Anthropic",
        documentationUrl: "https://docs.anthropic.com",
        capabilities: {
          streaming: true,
          toolCalling: true,
          vision: false,
          audio: false
        },
        auth: {
          kind: "apiKey",
          required: true,
          sources: ["environment", "runtime"]
        },
        configurationFields: [
          {
            key: "baseUrl",
            label: "Base URL",
            kind: "url",
            required: false,
            description: "Optional endpoint"
          }
        ]
      }
    ]);
  });

  test("normalizes model catalog from HPD-OS with capabilities limits cost and option schema", () => {
    expect(normalizeModelCatalog([
      {
        providerKey: " openai ",
        modelId: " gpt-5 ",
        displayName: " GPT-5 ",
        family: " GPT ",
        releaseDate: " 2026-01-01 ",
        status: "active",
        recommended: true,
        free: true,
        capabilities: {
          tools: true,
          reasoning: true,
          vision: true,
          audio: false,
          attachments: true,
          local: false
        },
        limits: {
          context: 128000,
          input: -1,
          output: 8192
        },
        cost: {
          input: 1.25,
          output: 10,
          cacheRead: 0,
          cacheWrite: -1
        },
        providerOptionsSchema: [
          {
            key: "reasoningEffort",
            label: "Reasoning effort",
            kind: "select",
            required: false,
            description: "Optional reasoning control"
          }
        ]
      },
      { providerKey: "", modelId: "bad" }
    ])).toEqual([
      {
        providerKey: "openai",
        modelId: "gpt-5",
        displayName: "GPT-5",
        family: "GPT",
        releaseDate: "2026-01-01",
        status: "active",
        recommended: true,
        free: true,
        capabilities: {
          tools: true,
          reasoning: true,
          vision: true,
          audio: false,
          attachments: true,
          local: false
        },
        limits: {
          context: 128000,
          output: 8192
        },
        cost: {
          input: 1.25,
          output: 10,
          cacheRead: 0
        },
        providerOptionsSchema: [
          {
            key: "reasoningEffort",
            label: "Reasoning effort",
            kind: "select",
            required: false,
            description: "Optional reasoning control"
          }
        ]
      }
    ]);
  });

  test("visible model catalog applies persisted visibility and model quality rules", () => {
    let state = defaultProviderModelUiState();
    state = setProviderVisibility(state, "ollama", "hidden");
    state = setModelVisibility(state, { providerKey: "google-ai", modelId: "gemini-alpha" }, "visible");

    expect(visibleModelCatalog(state, normalizeModelCatalog([
      {
        providerKey: "openai",
        modelId: "gpt-5",
        displayName: "GPT-5",
        status: "active",
        capabilities: {}
      },
      {
        providerKey: "ollama",
        modelId: "local-model",
        displayName: "Local model",
        recommended: true,
        capabilities: {}
      },
      {
        providerKey: "google-ai",
        modelId: "gemini-alpha",
        displayName: "Gemini Alpha",
        status: "alpha",
        capabilities: {}
      }
    ]))).toEqual([
      {
        providerKey: "google-ai",
        modelId: "gemini-alpha",
        displayName: "Gemini Alpha",
        status: "alpha",
        recommended: false,
        free: false,
        capabilities: {
          tools: false,
          reasoning: false,
          vision: false,
          audio: false,
          attachments: false,
          local: false
        },
        providerOptionsSchema: []
      },
      {
        providerKey: "openai",
        modelId: "gpt-5",
        displayName: "GPT-5",
        status: "active",
        recommended: false,
        free: false,
        capabilities: {
          tools: false,
          reasoning: false,
          vision: false,
          audio: false,
          attachments: false,
          local: false
        },
        providerOptionsSchema: []
      }
    ]);
  });

  test("model catalog filter combines text search with capability tags", () => {
    const models = normalizeModelCatalog([
      {
        providerKey: "openrouter",
        modelId: "qwen/qwen3-coder:free",
        displayName: "Qwen Coder Free",
        free: true,
        capabilities: { tools: true, reasoning: true }
      },
      {
        providerKey: "openai",
        modelId: "gpt-5",
        displayName: "GPT-5",
        capabilities: { tools: true, reasoning: true, vision: true }
      }
    ]);
    const qwen = models.find((model) => model.modelId === "qwen/qwen3-coder:free");
    const gpt = models.find((model) => model.modelId === "gpt-5");

    expect(qwen).toBeDefined();
    expect(gpt).toBeDefined();
    expect(modelMatchesFilter(qwen, { query: "qwen", tags: ["tools", "free"] })).toBe(true);
    expect(modelMatchesFilter(qwen, { query: "qwen", tags: ["vision"] })).toBe(false);
    expect(modelMatchesFilter(gpt, { query: "qwen", tags: ["tools"] })).toBe(false);
  });

  test("model picker rows only include connected providers", () => {
    let state = defaultProviderModelUiState();
    state = selectProviderModel(state, { providerKey: "openai", modelId: "gpt-5" });
    state = setFavoriteProviderModel(state, { providerKey: "anthropic", modelId: "claude-sonnet" }, true);

    const rows = buildModelPickerRows(state, normalizeModelCatalog([
      {
        providerKey: "openai",
        modelId: "gpt-5",
        displayName: "GPT-5",
        recommended: true,
        capabilities: { tools: true, reasoning: true }
      },
      {
        providerKey: "anthropic",
        modelId: "claude-sonnet",
        displayName: "Claude Sonnet",
        recommended: true,
        free: true,
        capabilities: { tools: true, reasoning: true, vision: true }
      },
      {
        providerKey: "google-ai",
        modelId: "gemini",
        displayName: "Gemini",
        capabilities: { tools: true }
      }
    ]), {
      openai: {
        providerKey: "openai",
        connected: true,
        source: "environment",
        removable: false,
        hasLocalCredential: false
      },
      anthropic: {
        providerKey: "anthropic",
        connected: false,
        source: "missing",
        removable: false,
        hasLocalCredential: false
      }
    }, "claude");

    expect(rows).toEqual([]);
  });

  test("model picker rows are grouped with favorites for connected providers", () => {
    let state = defaultProviderModelUiState();
    state = setFavoriteProviderModel(state, { providerKey: "anthropic", modelId: "claude-sonnet" }, true);

    const rows = buildModelPickerRows(state, normalizeModelCatalog([
      {
        providerKey: "openai",
        modelId: "gpt-5",
        displayName: "GPT-5",
        recommended: true,
        capabilities: { tools: true, reasoning: true }
      },
      {
        providerKey: "anthropic",
        modelId: "claude-sonnet",
        displayName: "Claude Sonnet",
        recommended: true,
        free: true,
        capabilities: { tools: true, reasoning: true, vision: true }
      }
    ]), {
      openai: {
        providerKey: "openai",
        connected: true,
        source: "environment",
        removable: false,
        hasLocalCredential: false
      },
      anthropic: {
        providerKey: "anthropic",
        connected: true,
        source: "local",
        removable: true,
        hasLocalCredential: true
      }
    }, "claude");

    expect(rows).toEqual([
      { kind: "section", label: "Favorites" },
      {
        kind: "model",
        providerKey: "anthropic",
        modelId: "claude-sonnet",
        providerName: "anthropic",
        displayName: "Claude Sonnet",
        label: "anthropic / Claude Sonnet",
        status: "ready",
        statusLabel: "local",
        badges: ["reasoning", "tools", "vision", "free"],
        favorite: true,
        recent: false
      }
    ]);
  });

  test("loads provider catalog from the HPD-OS endpoint instead of the HPD-Agent SDK", async () => {
    const calls = [];
    const fetchImpl = async (url, init) => {
      calls.push({ url, init });
      return new Response(JSON.stringify([
        {
          providerKey: "openai",
          displayName: "OpenAI",
          capabilities: { streaming: true, toolCalling: true, vision: true, audio: false },
          auth: { kind: "apiKey", required: true, sources: ["environment"] },
          configurationFields: []
        }
      ]), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      });
    };

    const providers = await fetchHpdosProviderCatalog(fetchImpl);

    expect(calls).toHaveLength(1);
    expect(calls[0].url).toBe(hpdosProviderCatalogEndpoint);
    expect(calls[0].init.method).toBe("GET");
    expect(providers).toEqual([
      {
        providerKey: "openai",
        displayName: "OpenAI",
        capabilities: { streaming: true, toolCalling: true, vision: true, audio: false },
        auth: { kind: "apiKey", required: true, sources: ["environment"] },
        configurationFields: []
      }
    ]);
  });

  test("loads model catalog from the HPD-OS endpoint instead of provider APIs", async () => {
    const calls = [];
    const fetchImpl = async (url, init) => {
      calls.push({ url, init });
      return new Response(JSON.stringify([
        {
          providerKey: "anthropic",
          modelId: "claude-sonnet",
          displayName: "Claude Sonnet",
          status: "active",
          capabilities: { tools: true, reasoning: true, vision: true, audio: false, attachments: true, local: false },
          providerOptionsSchema: []
        }
      ]), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      });
    };

    const models = await fetchHpdosModelCatalog(fetchImpl);

    expect(calls).toHaveLength(1);
    expect(calls[0].url).toBe(hpdosModelCatalogEndpoint);
    expect(calls[0].init.method).toBe("GET");
    expect(models).toEqual([
      {
        providerKey: "anthropic",
        modelId: "claude-sonnet",
        displayName: "Claude Sonnet",
        status: "active",
        free: false,
        recommended: false,
        capabilities: { tools: true, reasoning: true, vision: true, audio: false, attachments: true, local: false },
        providerOptionsSchema: []
      }
    ]);
  });

  test("normalizes provider detail and status without exposing credential values", () => {
    expect(normalizeProviderDetail({
      provider: {
        providerKey: "openai",
        displayName: "OpenAI",
        capabilities: { streaming: true },
        auth: { kind: "apiKey", required: true, sources: ["environment", "local"] },
        configurationFields: []
      },
      status: {
        providerKey: "openai",
        connected: true,
        source: "environment",
        removable: false,
        hasLocalCredential: true,
        value: "should-not-survive"
      }
    })).toEqual({
      provider: {
        providerKey: "openai",
        displayName: "OpenAI",
        capabilities: { streaming: true, toolCalling: false, vision: false, audio: false },
        auth: { kind: "apiKey", required: true, sources: ["environment", "local"] },
        configurationFields: []
      },
      status: {
        providerKey: "openai",
        connected: true,
        source: "environment",
        removable: false,
        hasLocalCredential: true
      }
    });

    expect(normalizeProviderStatus({
      providerKey: "anthropic",
      connected: false,
      source: "weird",
      removable: true,
      hasLocalCredential: false,
      message: " Missing "
    })).toEqual({
      providerKey: "anthropic",
      connected: false,
      source: "unknown",
      removable: true,
      hasLocalCredential: false,
      message: "Missing"
    });
  });

  test("provider status and credential helpers use HPD-OS endpoints", async () => {
    const calls = [];
    const fetchImpl = async (url, init) => {
      calls.push({ url, init });
      return new Response(JSON.stringify({
        providerKey: "openai",
        connected: true,
        source: "local",
        removable: true,
        hasLocalCredential: true
      }), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      });
    };

    await fetchHpdosProviderStatus("openai", fetchImpl);
    await saveHpdosProviderCredential("openai", { value: "secret", secretName: "ApiKey" }, fetchImpl);
    await deleteHpdosProviderCredential("openai", "ApiKey", fetchImpl);

    expect(calls.map((call) => [call.url, call.init.method])).toEqual([
      [`${hpdosProviderCatalogEndpoint}/openai/status`, "GET"],
      [`${hpdosProviderCatalogEndpoint}/openai/credential`, "PUT"],
      [`${hpdosProviderCatalogEndpoint}/openai/credential?secretName=ApiKey`, "DELETE"]
    ]);
    expect(JSON.parse(calls[1].init.body)).toEqual({ value: "secret", secretName: "ApiKey" });
  });

  test("provider detail helper loads catalog item plus status", async () => {
    const calls = [];
    const fetchImpl = async (url, init) => {
      calls.push({ url, init });
      return new Response(JSON.stringify({
        provider: {
          providerKey: "ollama",
          displayName: "Ollama",
          capabilities: { streaming: true, toolCalling: true, vision: false, audio: false },
          auth: { kind: "local", required: false, sources: ["local"] },
          configurationFields: []
        },
        status: {
          providerKey: "ollama",
          connected: true,
          source: "local",
          removable: false,
          hasLocalCredential: false
        }
      }), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      });
    };

    const detail = await fetchHpdosProviderDetail("ollama", fetchImpl);

    expect(calls).toHaveLength(1);
    expect(calls[0]).toMatchObject({
      url: `${hpdosProviderCatalogEndpoint}/ollama`,
      init: { method: "GET" }
    });
    expect(detail.status).toEqual({
      providerKey: "ollama",
      connected: true,
      source: "local",
      removable: false,
      hasLocalCredential: false
    });
  });
});
