/**
 * PermissionDialog Component
 *
 * Handles HPD PERMISSION_REQUEST events and provides a modal dialog
 * for users to approve or deny tool execution.
 *
 * @example
 * ```svelte
 * <script>
 *   import { createAgent } from '@hpd/hpd-agent-headless-ui';
 *   import * as PermissionDialog from '@hpd/hpd-agent-headless-ui/permission-dialog';
 *
 *   const agent = createAgent({ baseUrl: 'http://localhost:5135' });
 * </script>
 *
 * <PermissionDialog.Root {agent}>
 *   <PermissionDialog.Overlay />
 *   <PermissionDialog.Content>
 *     <PermissionDialog.Header>
 *       Permission Required
 *     </PermissionDialog.Header>
 *
 *     <PermissionDialog.Description>
 *       {#snippet children({ functionName, description })}
 *         <p>Allow <strong>{functionName}</strong>?</p>
 *         {#if description}
 *           <p>{description}</p>
 *         {/if}
 *       {/snippet}
 *     </PermissionDialog.Description>
 *
 *     <PermissionDialog.Actions>
 *       <PermissionDialog.Approve choice="allow_always">Always Allow</PermissionDialog.Approve>
 *       <PermissionDialog.Approve choice="allow_once">Allow Once</PermissionDialog.Approve>
 *       <PermissionDialog.Deny>Deny</PermissionDialog.Deny>
 *     </PermissionDialog.Actions>
 *   </PermissionDialog.Content>
 * </PermissionDialog.Root>
 * ```
 */

export * from './exports.ts';
