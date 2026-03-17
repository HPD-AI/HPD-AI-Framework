/**
 * AudioPlaybackGate Component
 *
 * Handles browser autoplay restrictions for Web Audio API.
 *
 * ## Usage
 *
 * ```svelte
 * <script>
 *   import * as AudioGate from '@hpd/hpd-agent-headless-ui/audio-playback-gate';
 * </script>
 *
 * <AudioGate.Root>
 *   {#snippet children({ canPlayAudio, enableAudio, status })}
 *     {#if !canPlayAudio}
 *       <button onclick={() => enableAudio()}>
 *         Enable Audio
 *       </button>
 *     {:else}
 *       <p>Audio is ready!</p>
 *     {/if}
 *   {/snippet}
 * </AudioGate.Root>
 * ```
 *
 * @module AudioPlaybackGate
 */

export * from './exports.ts';
