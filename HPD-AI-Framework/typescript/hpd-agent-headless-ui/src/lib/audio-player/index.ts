/**
 * AudioPlayer Component
 *
 * Handles TTS audio playback with streaming chunks.
 *
 * ## Features
 *
 * - **Streaming playback** - Plays audio chunks as they arrive
 * - **Out-of-order handling** - Sorts chunks by index automatically
 * - **Buffer threshold** - Starts playback after buffering 2+ chunks
 * - **Two modes:**
 *   - Simple mode (default): HTMLAudioElement for basic playback
 *   - Advanced mode: Web Audio API for volume control + visualization
 * - **Pause/resume** - Handle interruptions gracefully
 * - **Progress tracking** - currentTime, duration, progress (0-1)
 *
 * ## Usage
 *
 * ### Simple Mode (Default)
 *
 * ```svelte
 * <script>
 *   import * as AudioPlayer from '@hpd/hpd-agent-headless-ui/audio-player';
 * </script>
 *
 * <AudioPlayer.Root>
 *   {#snippet children({ status, progress, playing, pause, resume })}
 *     <div class="audio-player" data-status={status}>
 *       {#if playing}
 *         <button onclick={pause}>Pause</button>
 *       {:else}
 *         <button onclick={resume}>Resume</button>
 *       {/if}
 *
 *       <div class="progress-bar">
 *         <div class="progress" style="width: {progress * 100}%"></div>
 *       </div>
 *
 *       {#if status === 'buffering'}
 *         <span>Loading...</span>
 *       {/if}
 *     </div>
 *   {/snippet}
 * </AudioPlayer.Root>
 * ```
 *
 * ### Advanced Mode (Web Audio API)
 *
 * ```svelte
 * <AudioPlayer.Root useWebAudio={true}>
 *   {#snippet children({ status, progress, pause, resume, setVolume, analyserNode })}
 *     <div class="audio-player" data-status={status}>
 *       <button onclick={status === 'playing' ? pause : resume}>
 *         {status === 'playing' ? 'Pause' : 'Play'}
 *       </button>
 *
 *       <!-- Volume control (Web Audio mode only) -->
 *       <input
 *         type="range"
 *         min="0"
 *         max="1"
 *         step="0.1"
 *         oninput={(e) => setVolume(parseFloat(e.target.value))}
 *       />
 *
 *       <!-- Visualizer (uses analyserNode) -->
 *       {#if analyserNode}
 *         <AudioVisualizer {analyserNode} mode="bar" bands={5} />
 *       {/if}
 *     </div>
 *   {/snippet}
 * </AudioPlayer.Root>
 * ```
 *
 * ## Props
 *
 * - `useWebAudio?: boolean` - Use Web Audio API instead of HTMLAudioElement (default: false)
 * - `bufferThreshold?: number` - Number of chunks to buffer before playback (default: 2)
 * - `onStatusChange?: (status) => void` - Called when status changes
 * - `onError?: (error) => void` - Called when error occurs
 *
 * ## Snippet Props
 *
 * - `playing: boolean` - Whether audio is playing
 * - `paused: boolean` - Whether audio is paused
 * - `buffering: boolean` - Whether audio is buffering
 * - `currentTime: number` - Current playback position (seconds)
 * - `duration: number` - Total duration (seconds)
 * - `progress: number` - Playback progress (0-1)
 * - `status: AudioPlayerStatus` - Current status ('idle' | 'buffering' | 'playing' | 'paused' | 'error')
 * - `error: Error | null` - Error object if status is 'error'
 * - `pause: () => void` - Pause playback
 * - `resume: () => void` - Resume playback
 * - `stop: () => void` - Stop and clear queue
 * - `setVolume: (volume: number) => void` - Set volume 0-1 (Web Audio mode only)
 * - `analyserNode: AnalyserNode | null` - For visualization (Web Audio mode only)
 *
 * @module AudioPlayer
 */

export * from './exports.ts';
