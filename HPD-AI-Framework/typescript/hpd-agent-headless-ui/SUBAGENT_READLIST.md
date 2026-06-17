# HPD Agent Headless UI v2 Subagent Readlist

Before coding anything, read these files in order. The goal is to understand the real lower-level lifecycle, then salvage only useful behavior from the archived UI.

Key instruction: read the proposal first, then the client/backend contract, then the archived UI as salvage material. Do not copy the old workspace architecture back in.

## Core Architecture Context

- `PROPOSAL.md`
- `../hpd-agent-client/src/client.ts`
- `../hpd-agent-client/src/types/events.ts`
- `../hpd-agent-client/src/types/transport.ts`
- `../hpd-agent-client/src/transports/sse.ts`
- `../hpd-agent-client/src/transports/websocket.ts`
- `../hpd-agent-client/src/api.ts`
- `../hpd-agent-client/src/types/session.ts`
- `../hpd-agent-client/src/types/thread-run.ts`

## Backend Truth For Thread/Event Semantics

- `../../dotnet/HPD-Agent.Framework/src/HPD-Agent/Session/Thread.cs`
- `../../dotnet/HPD-Agent.Framework/src/HPD-Agent/Session/Session.cs`
- `../../dotnet/HPD-Agent.Framework/src/HPD-Agent/Session/ThreadEvents.cs`
- `../../dotnet/HPD-Agent.Framework/src/HPD-Agent/Session/ThreadProjector.cs`
- `../../dotnet/HPD-Agent.Framework/src/HPD-Agent/Session/ISessionStore.cs`
- `../../dotnet/HPD-Agent.Framework/src/HPD-Agent/Agent/AgentEvents.cs`
- `../../dotnet/HPD-Agent.Framework/src/HPD-Agent.AspNetCore/EndpointMapping/Endpoints/StreamingEndpoints.cs`
- `../../dotnet/HPD-Agent.Framework/src/HPD-Agent.AspNetCore/Streaming/SseEventHandler.cs`
- `../../dotnet/HPD-Agent.Framework/src/HPD-Agent.Hosting/Lifecycle/AgentStreamingService.cs`

## Archived UI Pieces To Salvage Carefully

- `archive/src/lib/agent/agent.svelte.ts`
- `archive/src/lib/agent/types.ts`
- `archive/src/lib/workspace/workspace.svelte.ts`
- `archive/src/lib/workspace/types.ts`

## Old Component Behavior To Preserve Later In Framework Adapters

- `archive/src/lib/message/message.svelte.ts`
- `archive/src/lib/message-list/message-list.svelte.ts`
- `archive/src/lib/chat-input/chat-input.svelte.ts`
- `archive/src/lib/permission-dialog/permission-dialog.svelte.ts`
- `archive/src/lib/tool-execution/tool-execution.svelte.ts`
- `archive/src/lib/run-config/run-config.svelte.ts`
- `archive/src/lib/thread-switcher/thread-switcher.svelte.ts`
- `archive/src/lib/session-list/session-list.svelte.ts`
- `archive/src/lib/file-attachment/file-attachment.svelte.ts`
- `archive/src/lib/artifact/artifact.svelte.ts`

## Archived Tests Worth Reading For Behavioral Intent

- `archive/src/lib/workspace/__tests__/workspace-transport.svelte.test.ts`
- `archive/src/lib/workspace/__tests__/workspace-send.svelte.test.ts`
- `archive/src/lib/workspace/__tests__/workspace-permissions.svelte.test.ts`
- `archive/src/lib/message/__tests__/message.test.ts`
- `archive/src/lib/permission-dialog/__tests__/permission-dialog.svelte.test.ts`

## Package/Build Shape

- `archive/package.json`
- `archive/tsconfig.json`
- `archive/vite.config.ts`

## What To Keep In Mind

- The core package is framework-neutral TypeScript.
- Framework-specific components belong in future adapter packages.
- Thread is the durable event stream identity.
- Rehydration and live projection are separate concerns.
- Live streams must be scoped to `{ agentId, sessionId, threadId }`.
- Do not route events into whatever thread happens to be active.
- Do not restore the old `WorkspaceImpl` as the main architecture.

