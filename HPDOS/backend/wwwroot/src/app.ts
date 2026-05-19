import { AgentClient } from "../../../../HPD-AI-Framework/typescript/hpd-agent-client/dist/index.js";
import { EventTypes } from "../../../../HPD-AI-Framework/typescript/hpd-agent-client/dist/types/events.js";
import type { AgentEvent } from "../../../../HPD-AI-Framework/typescript/hpd-agent-client/dist/types/events.js";
import type { AIContent, BranchMessage, ClientHarnessDefinition, ClientToolInvokeRequestEvent, ClientToolInvokeResponse } from "../../../../HPD-AI-Framework/typescript/hpd-agent-client/dist/index.js";

declare const marked: { parse(value: string): string; setOptions(options: Record<string, unknown>): void };
declare const DOMPurify: { sanitize(value: string): string };

const $ = (id: string) => document.getElementById(id);
const agentId = "hpdos-agent";
const branchId = "main";
const browserHarness: ClientHarnessDefinition = {
  name: "hpdos.browser",
  description: "Tools for inspecting the current HPD-OS browser shell and creating artifacts in the UI.",
  startCollapsed: false,
  tools: [
    {
      name: "get_active_view",
      description: "Return the active HPD-OS view and selected browser-shell context.",
      parametersSchema: {
        type: "object",
        properties: {},
        additionalProperties: false
      }
    },
    {
      name: "create_artifact",
      description: "Create or replace a browser-side artifact and show it inline in the chat.",
      parametersSchema: {
        type: "object",
        properties: {
          id: { type: "string", description: "Optional stable artifact id. A generated id is used when omitted." },
          title: { type: "string", description: "Short title shown in the artifact card." },
          type: { type: "string", enum: ["text", "markdown", "code", "html", "json"], description: "Artifact rendering type." },
          content: { type: "string", description: "Artifact content." },
          language: { type: "string", description: "Optional code language label." },
          open: { type: "boolean", description: "Whether to focus the artifact card immediately." }
        },
        required: ["title", "type", "content"],
        additionalProperties: false
      }
    },
    {
      name: "update_artifact",
      description: "Update an existing browser-side artifact.",
      parametersSchema: {
        type: "object",
        properties: {
          id: { type: "string" },
          title: { type: "string" },
          type: { type: "string", enum: ["text", "markdown", "code", "html", "json"] },
          content: { type: "string" },
          language: { type: "string" },
          open: { type: "boolean" }
        },
        required: ["id"],
        additionalProperties: false
      }
    },
    {
      name: "open_artifact",
      description: "Open an existing browser-side artifact by id.",
      parametersSchema: {
        type: "object",
        properties: { id: { type: "string" } },
        required: ["id"],
        additionalProperties: false
      }
    },
    {
      name: "list_artifacts",
      description: "List browser-side artifacts currently available in the shell.",
      parametersSchema: {
        type: "object",
        properties: {},
        additionalProperties: false
      }
    },
    {
      name: "close_artifact",
      description: "Unfocus the current inline artifact.",
      parametersSchema: {
        type: "object",
        properties: {},
        additionalProperties: false
      }
    }
  ]
};
const client = new AgentClient({
  baseUrl: "/api/hpd-agent",
  credentials: "include",
  onClientToolInvoke: handleClientToolInvoke
});

const chatState: {
  sessionId: string;
  toolNodes: Map<string, HTMLElement>;
  artifacts: Map<string, ArtifactRecord>;
  openArtifactId: string | null;
  assistant: HTMLElement | null;
} = {
  sessionId: localStorage.getItem("hpdos.sessionId") || "",
  toolNodes: new Map(),
  artifacts: new Map(),
  openArtifactId: null,
  assistant: null
};

type ArtifactType = "text" | "markdown" | "code" | "html" | "json";
type ArtifactView = "preview" | "code";

interface ArtifactRecord {
  id: string;
  title: string;
  type: ArtifactType;
  content: string;
  language?: string;
  createdAt: string;
  updatedAt: string;
}

marked.setOptions({ gfm: true, breaks: true });

client.on(EventTypes.TEXT_DELTA, (event) => {
  if (typeof event.text === "string") {
    renderMarkdownDelta(ensureAssistant(), event.text);
  }
});

client.on(EventTypes.MESSAGE_TURN_ERROR, (event) => {
  showChatError(new Error(event.message || "Message turn failed."));
});

client.on(EventTypes.TOOL_CALL_START, (event) => renderTool(event, "started"));
client.on(EventTypes.TOOL_CALL_ARGS, (event) => renderToolBlock(event.callId, "Args", jsonish(event.argsJson)));
client.on(EventTypes.TOOL_CALL_RESULT, (event) => {
  renderToolBlock(event.callId, "Result", event.result?.text || JSON.stringify(event.result || {}, null, 2));
});
client.onError(showChatError);

async function handleClientToolInvoke(request: ClientToolInvokeRequestEvent): Promise<ClientToolInvokeResponse> {
  const toolName = cleanClientToolName(request.toolName);
  if (toolName === "get_active_view") {
    return {
      requestId: request.requestId,
      success: true,
      content: [{
        type: "json",
        value: currentClientContext()
      }]
    };
  }
  if (toolName === "create_artifact") {
    const artifact = applyArtifactFunctionCall(toolName, request.arguments);
    if (!artifact) return errorToolResponse(request.requestId, "Failed to create artifact.");
    renderArtifactCard(artifact);
    if (request.arguments.open !== false) openArtifact(artifact.id);
    return jsonToolResponse(request.requestId, { artifact, opened: chatState.openArtifactId === artifact.id });
  }
  if (toolName === "update_artifact") {
    const id = stringArg(request.arguments, "id");
    if (!id || !chatState.artifacts.has(id)) return errorToolResponse(request.requestId, `Artifact not found: ${id || "(missing id)"}`);
    const artifact = applyArtifactFunctionCall(toolName, request.arguments);
    if (!artifact) return errorToolResponse(request.requestId, `Artifact not found: ${id}`);
    renderArtifactCard(artifact);
    if (request.arguments.open === true || chatState.openArtifactId === artifact.id) openArtifact(artifact.id);
    return jsonToolResponse(request.requestId, { artifact, opened: chatState.openArtifactId === artifact.id });
  }
  if (toolName === "open_artifact") {
    const id = stringArg(request.arguments, "id");
    if (!id || !chatState.artifacts.has(id)) return errorToolResponse(request.requestId, `Artifact not found: ${id || "(missing id)"}`);
    openArtifact(id);
    return jsonToolResponse(request.requestId, { id, opened: true });
  }
  if (toolName === "list_artifacts") {
    return jsonToolResponse(request.requestId, {
      openArtifactId: chatState.openArtifactId,
      artifacts: Array.from(chatState.artifacts.values()).map(({ id, title, type, language, updatedAt }) => ({ id, title, type, language, updatedAt }))
    });
  }
  if (toolName === "close_artifact") {
    closeArtifact();
    return jsonToolResponse(request.requestId, { opened: false });
  }

  return errorToolResponse(request.requestId, `Unknown client tool: ${request.toolName}`);
}

document.body.addEventListener("click", (event) => {
  const target = event.target as Element | null;
  const nav = target?.closest(".nav");
  if (nav) {
    document.querySelectorAll(".nav").forEach((node) => node.removeAttribute("aria-current"));
    nav.setAttribute("aria-current", "page");
  }

  if (target?.closest("[data-format-graph]")) formatGraphJson();
  const artifactViewButton = target?.closest<HTMLElement>("[data-artifact-view]");
  if (artifactViewButton) {
    const card = artifactViewButton.closest<HTMLElement>("[data-artifact-card]");
    const artifact = chatState.artifacts.get(card?.dataset.artifactCard || "");
    const view = artifactViewButton.dataset.artifactView === "code" ? "code" : "preview";
    if (artifact) renderArtifactCard(artifact, false, view);
    return;
  }
  const artifactButton = target?.closest<HTMLElement>("[data-artifact-id]");
  if (artifactButton) {
    openArtifact(artifactButton.dataset.artifactId || "");
  }
  if (target?.closest("[data-show-handlers]")) {
    $("handlerList")?.classList.toggle("hidden");
    $("graphPreview")?.classList.toggle("hidden");
  }
});

document.body.addEventListener("htmx:afterSwap", (event) => {
  if ((event as CustomEvent).detail.target.id === "view") wireChat();
  renderGraphPreview();
  autoHideToast();
});

document.body.addEventListener("input", (event) => {
  const target = event.target as HTMLTextAreaElement | HTMLInputElement | null;
  if (target?.id === "graphJson") debounceGraphPreview();
  if (target?.id === "text") {
    target.style.height = "auto";
    target.style.height = `${target.scrollHeight}px`;
  }
});

function wireChat() {
  const composer = $("composer") as HTMLFormElement | null;
  if (!composer || composer.dataset.wired) return;
  composer.dataset.wired = "true";
  composer.addEventListener("submit", submitChat);
  $("newSession")?.addEventListener("click", newSession);
  resetTurnState();
  void loadSessions();
  void hydrateSession();
}

async function newSession() {
  const session = await client.createSession();
  chatState.sessionId = session.id;
  localStorage.setItem("hpdos.sessionId", session.id);
  $("chatStack")?.replaceChildren();
  clearArtifacts();
  await loadSessions();
}

async function ensureSession() {
  if (!chatState.sessionId) await newSession();
}

async function loadSessions() {
  const sessionsNode = $("sessions");
  if (!sessionsNode) return;
  try {
    const sessions = await client.listSessions();
    const sessionCount = $("sessionCount");
    if (sessionCount) sessionCount.textContent = String(sessions.length);
    sessionsNode.innerHTML = sessions.sort((a, b) => new Date(b.lastActivity || b.createdAt || 0).getTime() - new Date(a.lastActivity || a.createdAt || 0).getTime()).map((session) => `
      <button class="mb-1 grid w-full rounded-hpd border px-3 py-2 text-left ${session.id === chatState.sessionId ? "border-blue-200 bg-blue-50" : "border-transparent hover:border-hpd-line hover:bg-white"}" data-session="${escapeHtml(session.id)}" type="button">
        <span class="truncate text-sm font-black">${escapeHtml(session.metadata?.title || `Chat ${String(session.id).slice(0, 6).toUpperCase()}`)}</span>
        <span class="mt-1 truncate text-xs font-semibold text-hpd-muted">${formatDate(session.lastActivity || session.createdAt)}</span>
      </button>`).join("") || '<div class="rounded-hpd border border-dashed border-hpd-line bg-white/70 p-3 text-sm text-hpd-muted">No recent sessions.</div>';
    sessionsNode.querySelectorAll<HTMLElement>("[data-session]").forEach((button) => button.addEventListener("click", () => switchSession(button.dataset.session || "")));
  } catch (error) {
    sessionsNode.innerHTML = `<div class="rounded-hpd border border-red-200 bg-red-50 p-3 text-sm text-red-700">${escapeHtml(messageOf(error))}</div>`;
  }
}

async function switchSession(id: string) {
  chatState.sessionId = id;
  localStorage.setItem("hpdos.sessionId", id);
  $("chatStack")?.replaceChildren();
  resetTurnState();
  clearArtifacts();
  await hydrateSession();
  await loadSessions();
}

async function hydrateSession() {
  if (!chatState.sessionId || !$("chatStack")) return;
  setBusy(true);
  try {
    const messages = await client.getBranchMessages(chatState.sessionId, branchId);
    $("chatStack")?.replaceChildren();
    clearArtifacts();
    for (const message of messages || []) hydrateMessage(message);
  } catch (error) {
    showChatError(error);
  } finally {
    setBusy(false);
  }
}

async function submitChat(event: SubmitEvent) {
  event.preventDefault();
  const textInput = $("text") as HTMLTextAreaElement | null;
  const text = textInput?.value.trim() || "";
  if (!text) return;
  appendMessage(text, "user");
  if (textInput) textInput.value = "";
  resetTurnState();
  setBusy(true);
  try {
    await sendChat(text);
  } catch (error) {
    showChatError(error);
  } finally {
    setBusy(false);
    await loadSessions();
  }
}

async function sendChat(text: string) {
  const providerKey = ($("provider") as HTMLInputElement | null)?.value.trim();
  const modelId = ($("model") as HTMLInputElement | null)?.value.trim();
  if (!providerKey || !modelId) throw new Error("Provider and model are required.");
  await ensureSession();
  await client.run({
    type: EventTypes.USER_TEXT_INPUT,
    agentId,
    sessionId: chatState.sessionId,
    branchId,
    text,
    runConfig: {
      providerKey,
      modelId,
      clientToolInput: {
        clientHarnesses: [browserHarness],
        context: [{
          key: "hpdos.activeView",
          description: "The current HPD-OS shell view.",
          value: currentClientContext()
        }]
      }
    }
  });
  if (!chatState.assistant) appendMessage("(no text output)", "assistant");
}

function currentClientContext() {
  const activeNav = document.querySelector<HTMLElement>(".nav[aria-current='page']");
  let graphId: string | undefined;
  try {
    const graph = JSON.parse(($("graphJson") as HTMLTextAreaElement | null)?.value || "{}");
    graphId = typeof graph.graphId === "string" ? graph.graphId : undefined;
  } catch {
    graphId = undefined;
  }

  return {
    activeView: activeNav?.getAttribute("hx-get")?.includes("workflows") ? "workflows" : "chat",
    sessionId: chatState.sessionId || undefined,
    graphId,
    openArtifactId: chatState.openArtifactId,
    artifactCount: chatState.artifacts.size
  };
}

function hydrateMessage(message: BranchMessage) {
  const role = String(message.role || "").toLowerCase();
  const text = (message.contents || [])
    .filter(isTextContent)
    .map((content) => content.text || "")
    .filter(Boolean)
    .join("\n");
  if (text && (role === "user" || role === "assistant")) {
    const node = appendMessage(role === "user" ? text : "", role);
    if (role === "assistant") renderMarkdownDelta(node, text);
  }

  for (const content of message.contents || []) {
    if (isFunctionCallContent(content)) {
      hydrateFunctionCall(content, message.timestamp);
    } else if (isFunctionResultContent(content)) {
      renderToolBlock(content.callId, "Result", content.result || "");
    }
  }
}

function isTextContent(content: AIContent): content is Extract<AIContent, { $type: "text" }> {
  return content.$type === "text";
}

function isFunctionCallContent(content: AIContent): content is Extract<AIContent, { $type: "functionCall" }> {
  return content.$type === "functionCall";
}

function isFunctionResultContent(content: AIContent): content is Extract<AIContent, { $type: "functionResult" }> {
  return content.$type === "functionResult";
}

function hydrateFunctionCall(content: Extract<AIContent, { $type: "functionCall" }>, timestamp: string) {
  const toolName = cleanClientToolName(content.name);
  if (isArtifactToolName(toolName)) {
    try {
      const artifact = applyArtifactFunctionCall(toolName, content.arguments || {}, timestamp);
      if (artifact) renderArtifactCard(artifact, false);
      updateArtifactCards();
    } catch {
      renderTool(
        { type: EventTypes.TOOL_CALL_START, callId: content.callId, name: content.name } as AgentEvent & { callId: string; name: string },
        "started"
      );
      renderToolBlock(content.callId, "Args", content.arguments || {});
    }
    return;
  }

  renderTool(
    { type: EventTypes.TOOL_CALL_START, callId: content.callId, name: content.name } as AgentEvent & { callId: string; name: string },
    "started"
  );
  if (content.arguments && Object.keys(content.arguments).length) renderToolBlock(content.callId, "Args", content.arguments);
}

function isArtifactToolName(toolName: string) {
  return toolName === "create_artifact"
    || toolName === "update_artifact"
    || toolName === "open_artifact"
    || toolName === "close_artifact"
    || toolName === "list_artifacts";
}

function applyArtifactFunctionCall(toolName: string, args: Record<string, unknown>, timestamp = new Date().toISOString()) {
  if (toolName === "create_artifact") {
    const artifact = upsertArtifact(args, true, timestamp);
    if (args.open !== false) chatState.openArtifactId = artifact.id;
    return artifact;
  }
  if (toolName === "update_artifact") {
    const artifact = upsertArtifact(args, false, timestamp);
    if (args.open === true || chatState.openArtifactId === artifact.id) chatState.openArtifactId = artifact.id;
    return artifact;
  }
  if (toolName === "open_artifact") {
    const id = stringArg(args, "id");
    if (id && chatState.artifacts.has(id)) {
      chatState.openArtifactId = id;
      return chatState.artifacts.get(id) || null;
    }
  }
  if (toolName === "close_artifact") {
    chatState.openArtifactId = null;
  }
  return null;
}

function upsertArtifact(args: Record<string, unknown>, create: boolean, timestamp = new Date().toISOString()): ArtifactRecord {
  const id = stringArg(args, "id") || `artifact-${crypto.randomUUID().slice(0, 8)}`;
  const previous = chatState.artifacts.get(id);
  const artifact: ArtifactRecord = {
    id,
    title: stringArg(args, "title") || previous?.title || "Untitled artifact",
    type: artifactTypeArg(args, "type") || previous?.type || "text",
    content: stringArg(args, "content") ?? previous?.content ?? "",
    language: stringArg(args, "language") || previous?.language,
    createdAt: previous?.createdAt || timestamp,
    updatedAt: timestamp
  };
  if (!create && !previous) throw new Error(`Artifact not found: ${id}`);
  chatState.artifacts.set(id, artifact);
  return artifact;
}

function renderArtifactCard(artifact: ArtifactRecord, shouldScroll = true, view?: ArtifactView) {
  const stack = $("chatStack");
  if (!stack) return;
  let card = document.querySelector<HTMLElement>(`[data-artifact-card="${cssEscape(artifact.id)}"]`);
  const selectedView: ArtifactView = view || (card?.dataset.artifactView === "code" ? "code" : "preview");
  if (!card) {
    const wrap = document.createElement("article");
    wrap.className = "flex justify-center";
    card = document.createElement("section");
    card.className = "artifact-card w-full max-w-4xl";
    card.dataset.artifactCard = artifact.id;
    card.dataset.artifactId = artifact.id;
    wrap.appendChild(card);
    stack.appendChild(wrap);
  }

  card.dataset.open = String(chatState.openArtifactId === artifact.id);
  card.dataset.artifactView = selectedView;
  card.innerHTML = `
    <div class="artifact-card-header">
      <div class="min-w-0">
        <div class="flex items-center gap-2">
          <span class="hpd-badge font-mono">${escapeHtml(artifactIcon(artifact.type))}</span>
          <h3 class="truncate text-sm font-black" data-artifact-title>${escapeHtml(artifact.title)}</h3>
        </div>
        <p class="mt-1 truncate text-xs font-semibold text-hpd-muted">${escapeHtml(artifact.type)}${artifact.language ? ` / ${escapeHtml(artifact.language)}` : ""}</p>
      </div>
      <div class="flex shrink-0 items-center gap-2">
        <div class="flex rounded-full border border-hpd-line bg-hpd-soft p-0.5">
          <button class="artifact-tab" data-artifact-view="preview" aria-current="${selectedView === "preview"}" type="button">Preview</button>
          <button class="artifact-tab" data-artifact-view="code" aria-current="${selectedView === "code"}" type="button">Code</button>
        </div>
        <span class="hpd-badge">${formatDate(artifact.updatedAt)}</span>
      </div>
    </div>
    <div class="artifact-card-body" data-artifact-content></div>
  `;
  const content = card.querySelector<HTMLElement>("[data-artifact-content]");
  if (content) renderArtifactContent(content, artifact, selectedView);
  if (shouldScroll) scrollChat();
}

function updateArtifactCards() {
  document.querySelectorAll<HTMLElement>("[data-artifact-card]").forEach((node) => {
    node.dataset.open = String(chatState.openArtifactId === node.dataset.artifactCard);
  });
}

function openArtifact(id: string) {
  const artifact = chatState.artifacts.get(id);
  if (!artifact) return;
  chatState.openArtifactId = id;
  renderArtifactCard(artifact, false);
  updateArtifactCards();
  document.querySelector<HTMLElement>(`[data-artifact-card="${cssEscape(id)}"]`)?.scrollIntoView({ block: "nearest", behavior: "smooth" });
}

function closeArtifact() {
  chatState.openArtifactId = null;
  updateArtifactCards();
}

function clearArtifacts() {
  chatState.artifacts.clear();
  closeArtifact();
  document.querySelectorAll("[data-artifact-card]").forEach((node) => node.closest("article")?.remove());
}

function renderArtifactContent(target: HTMLElement, artifact: ArtifactRecord, view: ArtifactView = "preview") {
  target.replaceChildren();
  const wrap = document.createElement("div");
  wrap.className = "artifact-render";
  if (view === "code") {
    const pre = document.createElement("pre");
    pre.textContent = artifact.type === "json" ? jsonish(artifact.content) : artifact.content;
    wrap.appendChild(pre);
  } else if (artifact.type === "markdown") {
    wrap.innerHTML = DOMPurify.sanitize(marked.parse(artifact.content));
  } else if (artifact.type === "html") {
    const frame = document.createElement("iframe");
    frame.className = "artifact-frame";
    frame.setAttribute("sandbox", "allow-scripts");
    frame.srcdoc = artifact.content;
    target.appendChild(frame);
    return;
  } else if (artifact.type === "json") {
    const pre = document.createElement("pre");
    pre.textContent = jsonish(artifact.content);
    wrap.appendChild(pre);
  } else if (artifact.type === "code") {
    const pre = document.createElement("pre");
    pre.textContent = artifact.content;
    wrap.appendChild(pre);
  } else {
    wrap.textContent = artifact.content;
  }
  target.appendChild(wrap);
}

function jsonToolResponse(requestId: string, value: unknown): ClientToolInvokeResponse {
  return { requestId, success: true, content: [{ type: "json", value }] };
}

function errorToolResponse(requestId: string, errorMessage: string): ClientToolInvokeResponse {
  return { requestId, success: false, content: [], errorMessage };
}

function stringArg(args: Record<string, unknown>, key: string): string | undefined {
  const value = args[key];
  return typeof value === "string" && value.trim() ? value.trim() : undefined;
}

function artifactTypeArg(args: Record<string, unknown>, key: string): ArtifactType | undefined {
  const value = stringArg(args, key);
  return value === "text" || value === "markdown" || value === "code" || value === "html" || value === "json" ? value : undefined;
}

function artifactIcon(type: ArtifactType) {
  if (type === "code") return "{}";
  if (type === "markdown") return "MD";
  if (type === "html") return "<>";
  if (type === "json") return "[]";
  return "T";
}

function cleanClientToolName(value: string) {
  return value.split(".").pop() || value;
}

function cssEscape(value: string) {
  return "CSS" in window && typeof CSS.escape === "function" ? CSS.escape(value) : value.replaceAll('"', '\\"');
}

function appendMessage(content: string, role: string) {
  const stack = $("chatStack");
  if (!stack) throw new Error("Chat stack is not mounted.");
  const wrap = document.createElement("article");
  wrap.className = `flex ${role === "user" ? "justify-end" : "justify-start"}`;
  const node = document.createElement("div");
  node.className = role === "user"
    ? "max-w-[78%] rounded-2xl rounded-tr-md bg-hpd-blue px-4 py-3 text-sm leading-6 text-white shadow-sm"
    : "message-markdown max-w-[82%] rounded-2xl rounded-tl-md border border-hpd-line bg-white px-4 py-3 text-sm leading-6 shadow-sm";
  node.textContent = content;
  node.dataset.markdown = "";
  wrap.appendChild(node);
  stack.appendChild(wrap);
  scrollChat();
  return node;
}

function ensureAssistant() {
  if (!chatState.assistant) chatState.assistant = appendMessage("", "assistant");
  return chatState.assistant;
}

function renderMarkdownDelta(node: HTMLElement, delta: string) {
  node.dataset.markdown = (node.dataset.markdown || "") + delta;
  node.innerHTML = DOMPurify.sanitize(marked.parse(node.dataset.markdown));
  scrollChat();
}

function renderTool(event: AgentEvent & { callId?: string; functionName?: string; name?: string }, suffix: string) {
  const stack = $("chatStack");
  if (!stack) return;
  const id = event.callId || crypto.randomUUID();
  const node = document.createElement("details");
  node.className = "rounded-hpd border border-hpd-line bg-white shadow-sm";
  node.innerHTML = `<summary class="cursor-pointer px-4 py-3 text-sm font-black">${escapeHtml(cleanName(event.functionName || event.name || "tool"))} ${suffix}</summary><div class="grid gap-2 border-t border-hpd-line p-3" data-body></div>`;
  stack.appendChild(node);
  chatState.toolNodes.set(id, node);
}

function renderToolBlock(id: string | undefined, label: string, value: unknown) {
  const toolId = id || crypto.randomUUID();
  if (!chatState.toolNodes.has(toolId)) {
    renderTool({ type: EventTypes.TOOL_CALL_START, callId: toolId, name: "tool" } as AgentEvent & { callId: string; name: string }, "event");
  }
  const body = chatState.toolNodes.get(toolId)?.querySelector("[data-body]");
  if (!body) return;
  const pre = document.createElement("pre");
  pre.className = "json-box max-h-56";
  pre.textContent = `${label}\n${String(value || "").slice(0, 12000)}`;
  body.appendChild(pre);
}

function resetTurnState() {
  chatState.toolNodes.clear();
  chatState.assistant = null;
}

function setBusy(busy: boolean) {
  const send = $("send") as HTMLButtonElement | null;
  const newSessionButton = $("newSession") as HTMLButtonElement | null;
  if (send) send.disabled = busy;
  if (newSessionButton) newSessionButton.disabled = busy;
}

function showChatError(error: unknown) {
  const node = appendMessage("", "assistant");
  node.classList.add("border-red-200", "bg-red-50", "text-red-800");
  node.textContent = messageOf(error);
}

let graphTimer: number | undefined;
function debounceGraphPreview() {
  clearTimeout(graphTimer);
  graphTimer = window.setTimeout(renderGraphPreview, 150);
}

function formatGraphJson() {
  const graphJson = $("graphJson") as HTMLTextAreaElement | null;
  if (!graphJson) return;
  try {
    graphJson.value = JSON.stringify(JSON.parse(graphJson.value || "{}"), null, 2);
    renderGraphPreview();
  } catch (error) {
    toast(messageOf(error));
  }
}

function renderGraphPreview() {
  const preview = $("graphPreview");
  const source = $("graphJson") as HTMLTextAreaElement | null;
  if (!preview || !source) return;
  let graph: any;
  try { graph = JSON.parse(source.value || "{}"); } catch { return; }
  preview.innerHTML = "";
  const ids = ["START", ...Object.keys(graph.nodes || {}), "END"];
  const nodes = { START: { id: "START", name: "Start", type: "Start" }, ...(graph.nodes || {}), END: { id: "END", name: "End", type: "End" } };
  const positions: Record<string, { x: number; y: number }> = {};
  ids.forEach((id, i) => positions[id] = { x: 32 + i * 190, y: 140 + (i % 2) * 100 });
  preview.style.minWidth = `${Math.max(640, ids.length * 210)}px`;
  for (const edge of graph.edges || []) drawEdge(preview, positions[edge.from], positions[edge.to]);
  for (const id of ids) {
    const node = nodes[id];
    const pos = positions[id];
    const div = document.createElement("div");
    div.className = "absolute grid w-40 gap-1 rounded-hpd border border-hpd-line bg-white p-3 shadow-hpd";
    div.style.left = `${pos.x}px`;
    div.style.top = `${pos.y}px`;
    div.innerHTML = `<strong class="truncate text-sm">${escapeHtml(node.name || node.id)}</strong><span class="hpd-badge">${escapeHtml(node.type || "Handler")}</span><code class="truncate text-xs text-hpd-muted">${escapeHtml(node.handlerName || node.id)}</code>`;
    preview.appendChild(div);
  }
}

function drawEdge(parent: HTMLElement, from?: { x: number; y: number }, to?: { x: number; y: number }) {
  if (!from || !to) return;
  const start = { x: from.x + 160, y: from.y + 36 };
  const end = { x: to.x, y: to.y + 36 };
  const dx = end.x - start.x;
  const dy = end.y - start.y;
  const line = document.createElement("div");
  line.className = "absolute h-0.5 origin-left bg-slate-400";
  line.style.left = `${start.x}px`;
  line.style.top = `${start.y}px`;
  line.style.width = `${Math.max(24, Math.hypot(dx, dy))}px`;
  line.style.transform = `rotate(${Math.atan2(dy, dx) * 180 / Math.PI}deg)`;
  parent.appendChild(line);
}

function toast(message: string) {
  const toastNode = $("toast");
  if (!toastNode) return;
  toastNode.textContent = message;
  toastNode.classList.remove("hidden");
  autoHideToast();
}

function autoHideToast() {
  const toastNode = $("toast");
  if (!toastNode || !toastNode.textContent?.trim()) return;
  toastNode.classList.remove("hidden");
  clearTimeout(autoHideToast.timer);
  autoHideToast.timer = window.setTimeout(() => toastNode.classList.add("hidden"), 3200);
}
autoHideToast.timer = 0;

function scrollChat() {
  requestAnimationFrame(() => $("chat")?.scrollTo({ top: $("chat")?.scrollHeight || 0, behavior: "smooth" }));
}

function escapeHtml(value: unknown) {
  return String(value ?? "").replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
}

function formatDate(value: unknown) {
  const date = new Date(String(value));
  return Number.isNaN(date.getTime()) ? "" : date.toLocaleString([], { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" });
}

function jsonish(value: unknown) {
  try { return JSON.stringify(JSON.parse(String(value)), null, 2); } catch { return String(value || ""); }
}

function cleanName(value: unknown) {
  return String(value || "unknown").split(".").pop()?.replace(/^tool_/, "").replace(/_[A-Za-z0-9-]{8,}$/, "") || "tool";
}

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : String(error);
}
