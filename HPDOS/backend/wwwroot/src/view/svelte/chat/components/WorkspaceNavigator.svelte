<script lang="ts">
  import type { Session } from "@hpd/hpd-agent-client";
  import type { ChatRuntimeController } from "../runtime/chatRuntime.svelte";

  type Props = {
    runtime: ChatRuntimeController;
  };

  let { runtime }: Props = $props();

  const activeWorkspaceId = $derived(runtime.workspace?.id ?? "");
  let workspacePickerId = $state<string | null>(null);
  let expandedWorkspaceIds = $state<Record<string, true>>({});
  let initializedWorkspaceExpansion = $state(false);
  const workspacePicker = $derived(runtime.workspaces.find((workspace) => workspace.id === workspacePickerId) ?? null);

  $effect(() => {
    if (initializedWorkspaceExpansion || !activeWorkspaceId) return;
    initializedWorkspaceExpansion = true;
    expandedWorkspaceIds = {
      ...expandedWorkspaceIds,
      [activeWorkspaceId]: true
    };
  });

  function toggleWorkspacePicker(workspaceId: string): void {
    workspacePickerId = workspacePickerId === workspaceId ? null : workspaceId;
  }

  function closeWorkspacePicker(): void {
    workspacePickerId = null;
  }

  async function openWorkspacePicker(workspaceId: string): Promise<void> {
    if (workspaceId !== activeWorkspaceId) {
      await runtime.switchWorkspace(workspaceId, { selectFirst: false });
    }

    toggleWorkspacePicker(workspaceId);
  }

  async function createWorkspaceSession(workspaceId: string): Promise<void> {
    if (workspaceId !== activeWorkspaceId) {
      await runtime.switchWorkspace(workspaceId, { selectFirst: false });
    }

    await runtime.createSession();
  }

  async function toggleWorkspace(workspaceId: string): Promise<void> {
    if (expandedWorkspaceIds[workspaceId]) {
      const { [workspaceId]: _, ...nextExpandedWorkspaceIds } = expandedWorkspaceIds;
      expandedWorkspaceIds = nextExpandedWorkspaceIds;
      return;
    }

    expandedWorkspaceIds = { ...expandedWorkspaceIds, [workspaceId]: true };

    if (workspaceId !== activeWorkspaceId) {
      await runtime.switchWorkspace(workspaceId, { selectFirst: false });
    }
  }
</script>

<section class="hpd-chat-workspace-nav" aria-label="Workspaces">
  <div class="hpd-chat-workspace-nav-header">
    <button class="hpd-chat-workspace-title" type="button" disabled>
      <span>Workspaces</span>
    </button>
    <div class="hpd-chat-workspace-actions">
      <button
        class="hpd-chat-sidebar-icon-button"
        type="button"
        title="New workspace"
        aria-label="New workspace"
        disabled={runtime.loading}
        onclick={() => void runtime.createWorkspaceFromPicker()}
      >
        {@render PlusIcon()}
      </button>
      <button
        class="hpd-chat-sidebar-icon-button"
        type="button"
        title="New session"
        aria-label="New session"
        disabled={runtime.loading || runtime.sessions?.loading || !runtime.workspace}
        onclick={() => void runtime.createSession()}
      >
        {@render ChatPlusIcon()}
      </button>
    </div>
  </div>

  {#if runtime.error}
    <p class="hpd-chat-sidebar-error">{runtime.error}</p>
  {:else if runtime.loading}
    <p class="hpd-chat-sidebar-muted">Loading workspaces</p>
  {:else}
    {#if workspacePicker}
      <div class="hpd-chat-workspace-picker" role="dialog" aria-label={`${workspacePicker.name ?? workspacePicker.id} directories`}>
        <div class="hpd-chat-workspace-picker-header">
          <div>
            <strong>{workspacePicker.name ?? workspacePicker.id}</strong>
            <span>{workspacePicker.roots.length} {workspacePicker.roots.length === 1 ? "directory" : "directories"}</span>
          </div>
          <button
            class="hpd-chat-sidebar-icon-button"
            type="button"
            aria-label="Close workspace directories"
            title="Close"
            onclick={closeWorkspacePicker}
          >
            {@render XIcon()}
          </button>
        </div>

        <div class="hpd-chat-workspace-picker-roots">
          {#each workspacePicker.roots as root (root.id)}
            <div class="hpd-chat-workspace-picker-root" data-default={root.id === workspacePicker.defaultRootId}>
              <button
                type="button"
                title={`Use ${root.label ?? root.id} as default root`}
                onclick={() => void runtime.setActiveWorkspaceDefaultRoot(root.id)}
              >
                <span>{root.label ?? root.id}</span>
                <small>{root.path}</small>
              </button>
              <button
                class="hpd-chat-sidebar-icon-button"
                type="button"
                title={`Remove ${root.label ?? root.id}`}
                aria-label={`Remove ${root.label ?? root.id}`}
                disabled={workspacePicker.roots.length <= 1}
                onclick={() => void runtime.removeRootFromActiveWorkspace(root.id)}
              >
                {@render XIcon()}
              </button>
            </div>
          {/each}
        </div>

        <button
          class="hpd-chat-workspace-picker-add"
          type="button"
          onclick={() => void runtime.addRootsToActiveWorkspaceFromPicker()}
        >
          {@render PlusIcon()}
          <span>Add directory</span>
        </button>
      </div>
    {/if}

    <div class="hpd-chat-workspace-list">
      {#each runtime.workspaces as workspace (workspace.id)}
        <div class="hpd-chat-workspace-group" data-active={workspace.id === activeWorkspaceId}>
          <div class="hpd-chat-workspace-row-wrap">
            <button
              class="hpd-chat-workspace-row"
              type="button"
              aria-current={workspace.id === activeWorkspaceId ? "page" : undefined}
              aria-expanded={Boolean(expandedWorkspaceIds[workspace.id])}
              onclick={() => void toggleWorkspace(workspace.id)}
            >
              {@render FolderIcon()}
              <span>{workspace.name ?? workspace.id}</span>
            </button>
            <button
              class="hpd-chat-sidebar-icon-button hpd-chat-workspace-config-button"
              type="button"
              title={`New session in ${workspace.name ?? workspace.id}`}
              aria-label={`New session in ${workspace.name ?? workspace.id}`}
              disabled={runtime.loading || runtime.sessions?.loading}
              onclick={() => void createWorkspaceSession(workspace.id)}
            >
              {@render WriteIcon()}
            </button>
            <button
              class="hpd-chat-sidebar-icon-button hpd-chat-workspace-config-button"
              type="button"
              title={`Manage ${workspace.name ?? workspace.id} directories`}
              aria-label={`Manage ${workspace.name ?? workspace.id} directories`}
              aria-expanded={workspacePickerId === workspace.id}
              onclick={() => void openWorkspacePicker(workspace.id)}
            >
              {@render MoreIcon()}
            </button>
          </div>

          {#if expandedWorkspaceIds[workspace.id]}
            <div class="hpd-chat-workspace-sessions">
              {#each runtime.workspaceSessions[workspace.id] ?? [] as session (session.id)}
                {@render SessionButton(session, workspace.id)}
              {:else}
                <p class="hpd-chat-sidebar-muted">No sessions</p>
              {/each}
            </div>
          {/if}
        </div>
      {:else}
        <div class="hpd-chat-workspace-empty">
          <p>Choose a workspace before starting a coding session.</p>
          <button type="button" onclick={() => void runtime.createWorkspaceFromPicker()}>
            {@render PlusIcon()}
            <span>Add workspace</span>
          </button>
        </div>
      {/each}
    </div>

    <div class="hpd-chat-session-section">
      <div class="hpd-chat-session-section-header">
        <h2>Sessions</h2>
        <button
          class="hpd-chat-sidebar-icon-button"
          type="button"
          title="New session without workspace"
          aria-label="New session without workspace"
          disabled={runtime.loading}
          onclick={() => void runtime.createUnscopedSession()}
        >
          {@render WriteIcon()}
        </button>
      </div>
      <div class="hpd-chat-workspace-sessions">
        {#each runtime.unscopedSessions as session (session.id)}
          {@render SessionButton(session, sessionWorkspaceId(session))}
        {:else}
          <p class="hpd-chat-sidebar-muted">No sessions without a workspace</p>
        {/each}
      </div>
    </div>
  {/if}
</section>

{#snippet SessionButton(session: Session, workspaceId?: string)}
  <div class="hpd-chat-session-item" data-pinned={isPinned(session)}>
    <button
      class="hpd-chat-session-main"
      type="button"
      aria-current={runtime.activeSessionId === session.id ? "page" : undefined}
      onclick={() => void runtime.selectSession(session.id, workspaceId)}
    >
      <span>{sessionTitle(session)}</span>
    </button>
    <small class="hpd-chat-session-time">{formatLastActive(session.lastActivity)}</small>
    <div class="hpd-chat-session-actions" aria-label={`${sessionTitle(session)} actions`}>
      <button
        type="button"
        title={isPinned(session) ? "Unpin session" : "Pin session"}
        aria-label={isPinned(session) ? `Unpin ${sessionTitle(session)}` : `Pin ${sessionTitle(session)}`}
        aria-pressed={isPinned(session)}
        onclick={() => void runtime.toggleSessionPinned(session)}
      >
        {@render PinIcon()}
      </button>
      <button
        type="button"
        title="Delete session"
        aria-label={`Delete ${sessionTitle(session)}`}
        onclick={() => void runtime.deleteSession(session.id)}
      >
        {@render TrashIcon()}
      </button>
    </div>
  </div>
{/snippet}

{#snippet FolderIcon()}
  <svg class="hpd-chat-sidebar-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none">
    <path d="M3.5 6.5A2.5 2.5 0 0 1 6 4h4l2 2h6A2.5 2.5 0 0 1 20.5 8.5v7A2.5 2.5 0 0 1 18 18H6a2.5 2.5 0 0 1-2.5-2.5v-9Z" />
  </svg>
{/snippet}

{#snippet MoreIcon()}
  <svg class="hpd-chat-sidebar-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none">
    <path d="M12 6h.01" />
    <path d="M12 12h.01" />
    <path d="M12 18h.01" />
  </svg>
{/snippet}

{#snippet PlusIcon()}
  <svg class="hpd-chat-sidebar-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none">
    <path d="M12 5v14" />
    <path d="M5 12h14" />
  </svg>
{/snippet}

{#snippet WriteIcon()}
  <svg class="hpd-chat-sidebar-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none">
    <path d="M12 20h9" />
    <path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z" />
  </svg>
{/snippet}

{#snippet ChatPlusIcon()}
  <svg class="hpd-chat-sidebar-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none">
    <path d="M5 6.5A3.5 3.5 0 0 1 8.5 3h7A3.5 3.5 0 0 1 19 6.5v4A3.5 3.5 0 0 1 15.5 14H11l-4 4v-4.25A3.5 3.5 0 0 1 5 10.5v-4Z" />
    <path d="M12 6v5" />
    <path d="M9.5 8.5h5" />
  </svg>
{/snippet}

{#snippet XIcon()}
  <svg class="hpd-chat-sidebar-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none">
    <path d="M7 7l10 10" />
    <path d="M17 7 7 17" />
  </svg>
{/snippet}

{#snippet PinIcon()}
  <svg class="hpd-chat-sidebar-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none">
    <path d="M12 17v5" />
    <path d="M5 17h14" />
    <path d="M7 17l2-8" />
    <path d="M15 9l2 8" />
    <path d="M8 4h8" />
    <path d="M10 4v5" />
    <path d="M14 4v5" />
    <path d="M9 9h6" />
  </svg>
{/snippet}

{#snippet TrashIcon()}
  <svg class="hpd-chat-sidebar-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none">
    <path d="M3 6h18" />
    <path d="M8 6V4h8v2" />
    <path d="M19 6l-1 14H6L5 6" />
    <path d="M10 11v5" />
    <path d="M14 11v5" />
  </svg>
{/snippet}

<script lang="ts" module>
  function sessionTitle(session: Session): string {
    const title = session.metadata?.title;
    return typeof title === "string" && title.trim().length > 0
      ? title
      : `Chat ${session.id.slice(0, 8)}`;
  }

  function formatLastActive(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return "";

    const now = new Date();
    if (date.toDateString() === now.toDateString()) {
      return date.toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" });
    }

    const yesterday = new Date(now);
    yesterday.setDate(now.getDate() - 1);
    if (date.toDateString() === yesterday.toDateString()) {
      return "Yesterday";
    }

    const ageMs = now.getTime() - date.getTime();
    if (ageMs > 0 && ageMs < 7 * 24 * 60 * 60 * 1000) {
      return date.toLocaleDateString(undefined, { weekday: "short" });
    }

    return date.toLocaleDateString(undefined, { month: "short", day: "numeric" });
  }

  function sessionWorkspaceId(session: Session): string | undefined {
    const workspaceId = session.metadata?.workspaceId;
    return typeof workspaceId === "string" ? workspaceId : undefined;
  }

  function isPinned(session: Session): boolean {
    return session.metadata?.pinned === true;
  }
</script>
