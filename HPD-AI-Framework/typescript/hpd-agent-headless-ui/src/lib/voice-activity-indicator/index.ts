/**
 * VoiceActivityIndicator Component
 *
 * Provides visual feedback when user is speaking via voice activity detection (VAD).
 *
 * ## Features
 *
 * - **Active/inactive state** - Shows when user is speaking
 * - **Speech probability** - Confidence level of voice detection (0-1)
 * - **Intensity levels** - High/medium/low intensity based on probability
 * - **Duration tracking** - Tracks how long user spoke
 *
 * ## Usage
 *
 * ### Basic Example
 *
 * ```svelte
 * <script>
 *   import * as VoiceActivityIndicator from '@hpd/hpd-agent-headless-ui/voice-activity-indicator';
 * </script>
 *
 * <VoiceActivityIndicator.Root>
 *   {#snippet children({ active, intensityLevel })}
 *     <div class="vad-indicator" data-active={active} data-intensity={intensityLevel}>
 *       {#if active}
 *         <div class="pulse-animation"></div>
 *         <span>Listening...</span>
 *       {:else}
 *         <span>Silent</span>
 *       {/if}
 *     </div>
 *   {/snippet}
 * </VoiceActivityIndicator.Root>
 * ```
 *
 * ### With Speech Probability
 *
 * ```svelte
 * <VoiceActivityIndicator.Root>
 *   {#snippet children({ active, speechProbability, intensityLevel })}
 *     <div class="vad-indicator">
 *       {#if active}
 *         <div class="meter" data-intensity={intensityLevel}>
 *           <div class="fill" style="width: {speechProbability * 100}%"></div>
 *         </div>
 *         <span>Confidence: {(speechProbability * 100).toFixed(0)}%</span>
 *       {/if}
 *     </div>
 *   {/snippet}
 * </VoiceActivityIndicator.Root>
 * ```
 *
 * ### With Duration Display
 *
 * ```svelte
 * <VoiceActivityIndicator.Root>
 *   {#snippet children({ active, duration })}
 *     <div class="vad-indicator">
 *       {#if active}
 *         <span class="status">🎤 Speaking...</span>
 *       {:else if duration > 0}
 *         <span class="status">Spoke for {duration.toFixed(1)}s</span>
 *       {:else}
 *         <span class="status">Silent</span>
 *       {/if}
 *     </div>
 *   {/snippet}
 * </VoiceActivityIndicator.Root>
 * ```
 *
 * ## Props
 *
 * - `onActivityChange?: (active: boolean) => void` - Called when voice activity changes
 *
 * ## Snippet Props
 *
 * - `active: boolean` - Whether user is currently speaking
 * - `speechProbability: number` - Speech confidence (0-1)
 * - `duration: number` - Duration of last speech in seconds
 * - `intensityLevel: 'high' | 'medium' | 'low' | 'none'` - Intensity level
 *
 * ## Data Attributes
 *
 * - `data-active` - Present when user is speaking
 * - `data-intensity` - Intensity level (high/medium/low/none)
 *
 * ## Accessibility
 *
 * - `role="status"` - Announces changes to screen readers
 * - `aria-label="Voice activity"` - Describes the indicator
 * - `aria-live="polite"` - Announces activity changes politely
 *
 * @module VoiceActivityIndicator
 */

export * from './exports.ts';
