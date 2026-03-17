/**
 * SessionList - Compound headless component for session management
 *
 * @example
 * ```svelte
 * <script>
 *   import { SessionList } from '@hpd/hpd-agent-headless-ui';
 *
 *   let sessions = $state([]);
 *   let activeSessionId = $state(null);
 * </script>
 *
 * <SessionList.Root {sessions} bind:activeSessionId onSelect={...} onDelete={...} onCreate={...}>
 *   {#snippet children({ isEmpty, count })}
 *     <SessionList.CreateButton>New Session</SessionList.CreateButton>
 *     {#if isEmpty}
 *       <SessionList.Empty>No sessions yet</SessionList.Empty>
 *     {:else}
 *       {#each sessions as session (session.id)}
 *         <SessionList.Item {session}>
 *           {#snippet children({ isActive, lastActivity })}
 *             <span>{session.id.substring(0, 8)}</span>
 *             <span>{lastActivity}</span>
 *           {/snippet}
 *         </SessionList.Item>
 *       {/each}
 *     {/if}
 *   {/snippet}
 * </SessionList.Root>
 * ```
 */

export * from './exports.ts';

export {
	SessionListRootState,
	SessionListItemState,
	SessionListEmptyState,
	SessionListCreateButtonState,
	sessionListAttrs,
} from './session-list.svelte.js';

export type * from './types.js';
