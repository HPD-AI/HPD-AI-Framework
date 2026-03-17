/**
 * Transcription Component
 *
 * Displays live STT (speech-to-text) transcription with interim and final states.
 *
 * ## Features
 *
 * - **Interim text** - Shows text while user is speaking
 * - **Final text** - Shows completed transcription
 * - **Confidence levels** - High/medium/low confidence indicators
 * - **Auto-clear** - Clear transcription with `clear()` method
 *
 * ## Usage
 *
 * ### Basic Example
 *
 * ```svelte
 * <script>
 *   import * as Transcription from '@hpd/hpd-agent-headless-ui/transcription';
 * </script>
 *
 * <Transcription.Root>
 *   {#snippet children({ text, isFinal, confidenceLevel })}
 *     <div class="transcription" class:final={isFinal} data-confidence={confidenceLevel}>
 *       <p>{text}</p>
 *       {#if !isFinal}
 *         <span class="interim-indicator">...</span>
 *       {/if}
 *     </div>
 *   {/snippet}
 * </Transcription.Root>
 * ```
 *
 * ### With Clear Button
 *
 * ```svelte
 * <Transcription.Root>
 *   {#snippet children({ text, isFinal, isEmpty, clear })}
 *     <div class="transcription">
 *       {#if !isEmpty}
 *         <p>{text}</p>
 *         <button onclick={clear}>Clear</button>
 *       {:else}
 *         <p class="empty">Start speaking...</p>
 *       {/if}
 *     </div>
 *   {/snippet}
 * </Transcription.Root>
 * ```
 *
 * ### With Confidence Display
 *
 * ```svelte
 * <Transcription.Root>
 *   {#snippet children({ text, confidence, confidenceLevel })}
 *     <div class="transcription">
 *       <p>{text}</p>
 *       {#if confidence}
 *         <div class="confidence" data-level={confidenceLevel}>
 *           Confidence: {(confidence * 100).toFixed(0)}%
 *         </div>
 *       {/if}
 *     </div>
 *   {/snippet}
 * </Transcription.Root>
 * ```
 *
 * ## Props
 *
 * - `onTextChange?: (text: string, isFinal: boolean) => void` - Called when text changes
 * - `onClear?: () => void` - Called when transcription is cleared
 *
 * ## Snippet Props
 *
 * - `text: string` - Current transcription text
 * - `isFinal: boolean` - Whether transcription is final (completed)
 * - `confidence: number | null` - Confidence score (0-1)
 * - `confidenceLevel: 'high' | 'medium' | 'low' | null` - Confidence level
 * - `isEmpty: boolean` - Whether transcription is empty
 * - `clear: () => void` - Clear the transcription
 *
 * ## Data Attributes
 *
 * - `data-final` - Present when transcription is final
 * - `data-confidence` - Confidence level (high/medium/low)
 * - `data-empty` - Present when transcription is empty
 *
 * ## Accessibility
 *
 * - `role="status"` - Announces changes to screen readers
 * - `aria-label="Voice transcription"` - Describes the transcription area
 * - `aria-live="polite"` - Announces text changes politely
 * - `aria-busy` - True when transcription is not final
 *
 * @module Transcription
 */

export * from './exports.ts';
