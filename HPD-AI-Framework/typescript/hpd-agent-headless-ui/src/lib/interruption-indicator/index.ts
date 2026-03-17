/**
 * InterruptionIndicator Component
 *
 * Provides visual feedback when agent speech is paused or interrupted during conversation.
 *
 * ## Features
 *
 * - **Interruption detection** - Shows when user interrupts agent
 * - **Pause tracking** - Tracks why agent paused (user speaking, potential interruption)
 * - **Duration tracking** - Tracks how long agent was paused
 * - **Status states** - Normal, paused, or interrupted
 *
 * ## Usage
 *
 * ### Basic Example
 *
 * ```svelte
 * <script>
 *   import * as InterruptionIndicator from '@hpd/hpd-agent-headless-ui/interruption-indicator';
 * </script>
 *
 * <InterruptionIndicator.Root>
 *   {#snippet children({ status, interrupted, paused })}
 *     <div class="interruption-indicator" data-status={status}>
 *       {#if interrupted}
 *         <span class="badge interrupted">Interrupted</span>
 *       {:else if paused}
 *         <span class="badge paused">Paused</span>
 *       {:else}
 *         <span class="badge normal">Speaking</span>
 *       {/if}
 *     </div>
 *   {/snippet}
 * </InterruptionIndicator.Root>
 * ```
 *
 * ### With Pause Reason
 *
 * ```svelte
 * <InterruptionIndicator.Root>
 *   {#snippet children({ status, pauseReason, interruptedText })}
 *     <div class="interruption-indicator" data-status={status}>
 *       {#if status === 'interrupted'}
 *         <div class="interruption-banner">
 *           <strong>Interrupted:</strong> {interruptedText}
 *         </div>
 *       {:else if status === 'paused'}
 *         <div class="pause-banner" data-reason={pauseReason}>
 *           {#if pauseReason === 'user_speaking'}
 *             Agent paused - User is speaking
 *           {:else if pauseReason === 'potential_interruption'}
 *             Agent paused - Possible interruption detected
 *           {/if}
 *         </div>
 *       {/if}
 *     </div>
 *   {/snippet}
 * </InterruptionIndicator.Root>
 * ```
 *
 * ### With Duration Display
 *
 * ```svelte
 * <InterruptionIndicator.Root>
 *   {#snippet children({ status, pauseDuration, isNormal })}
 *     <div class="interruption-indicator">
 *       {#if !isNormal && pauseDuration > 0}
 *         <span class="duration">
 *           Resumed after {pauseDuration.toFixed(1)}s pause
 *         </span>
 *       {/if}
 *     </div>
 *   {/snippet}
 * </InterruptionIndicator.Root>
 * ```
 *
 * ### With Callbacks
 *
 * ```svelte
 * <script>
 *   function handleInterruptionChange(interrupted: boolean) {
 *     console.log('Interruption changed:', interrupted);
 *     if (interrupted) {
 *       // Show notification, pause animations, etc.
 *     }
 *   }
 *
 *   function handlePauseChange(paused: boolean) {
 *     console.log('Pause changed:', paused);
 *   }
 * </script>
 *
 * <InterruptionIndicator.Root
 *   onInterruptionChange={handleInterruptionChange}
 *   onPauseChange={handlePauseChange}
 * >
 *   {#snippet children({ status })}
 *     <!-- Your UI -->
 *   {/snippet}
 * </InterruptionIndicator.Root>
 * ```
 *
 * ## Props
 *
 * - `onInterruptionChange?: (interrupted: boolean) => void` - Called when interruption state changes
 * - `onPauseChange?: (paused: boolean) => void` - Called when pause state changes
 *
 * ## Snippet Props
 *
 * - `interrupted: boolean` - Whether agent is currently interrupted
 * - `paused: boolean` - Whether agent speech is currently paused
 * - `pauseReason: 'user_speaking' | 'potential_interruption' | null` - Why agent paused
 * - `pauseDuration: number` - Duration of last pause in seconds
 * - `interruptedText: string` - Transcribed text that caused interruption
 * - `status: 'interrupted' | 'paused' | 'normal'` - Overall interruption status
 * - `isInterrupted: boolean` - Helper for status === 'interrupted'
 * - `isPaused: boolean` - Helper for status === 'paused'
 * - `isNormal: boolean` - Helper for status === 'normal'
 *
 * ## Data Attributes
 *
 * - `data-interruption-indicator` - Present on root element
 * - `data-status` - Current status (interrupted/paused/normal)
 * - `data-interrupted` - Present when interrupted
 * - `data-paused` - Present when paused
 * - `data-pause-reason` - Reason for pause (user_speaking/potential_interruption)
 *
 * ## Accessibility
 *
 * - `role="status"` - Announces changes to screen readers
 * - `aria-label="Interruption status"` - Describes the indicator
 * - `aria-live="assertive"` - Announces interruptions immediately (high priority)
 *
 * ## HPD Protocol Events
 *
 * This component responds to these HPD protocol events:
 *
 * - `USER_INTERRUPTED` - User interrupted agent speech
 * - `SPEECH_PAUSED` - Agent paused speaking
 * - `SPEECH_RESUMED` - Agent resumed speaking
 *
 * @module InterruptionIndicator
 */

export * from './exports.ts';
