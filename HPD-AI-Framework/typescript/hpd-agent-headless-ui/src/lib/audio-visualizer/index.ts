/**
 * AudioVisualizer Component
 *
 * Provides real-time audio visualization using multiband frequency analysis.
 *
 * ## Features
 *
 * - **Multiband frequency analysis** - 5-7 frequency bands ( pattern)
 * - **Multiple modes** - Bar, waveform, radial visualizations
 * - **Real-time updates** - 60fps via requestAnimationFrame
 * - **Web Audio API** - Uses AnalyserNode for frequency data
 * - **Normalized volumes** - 0-1 range per band for easy styling
 *
 * ## Usage
 *
 * ### Basic Example (Bar Mode)
 *
 * ```svelte
 * <script>
 *   import * as AudioVisualizer from '@hpd/hpd-agent-headless-ui/audio-visualizer';
 *
 *   let analyserNode: AnalyserNode;
 *   let visualizerState;
 *
 *   // Get analyser from AudioPlayer or microphone
 *   $effect(() => {
 *     if (analyserNode && visualizerState) {
 *       visualizerState.startVisualization(analyserNode);
 *     }
 *   });
 * </script>
 *
 * <AudioVisualizer.Root bands={5} mode="bar">
 *   {#snippet children(state)}
 *     {#if state !== undefined}
 *       {@const _ = visualizerState || (visualizerState = state)}
 *     {/if}
 *
 *     <div class="visualizer">
 *       {#each state.volumes as volume, i}
 *         <div
 *           class="bar"
 *           style="height: {volume * 100}%"
 *           data-band={i}
 *         ></div>
 *       {/each}
 *     </div>
 *   {/snippet}
 * </AudioVisualizer.Root>
 * ```
 *
 * ### With Normalized Heights
 *
 * ```svelte
 * <AudioVisualizer.Root bands={7} mode="bar">
 *   {#snippet children({ volumes, maxVolume })}
 *     <div class="visualizer">
 *       {#each volumes as volume, i}
 *         <div
 *           class="bar"
 *           style="height: {(volume / maxVolume) * 100}%"
 *           data-band={i}
 *         ></div>
 *       {/each}
 *     </div>
 *   {/snippet}
 * </AudioVisualizer.Root>
 * ```
 *
 * ### With Color Gradients
 *
 * ```svelte
 * <AudioVisualizer.Root bands={5}>
 *   {#snippet children({ volumes })}
 *     <div class="visualizer">
 *       {#each volumes as volume, i}
 *         {@const intensity = Math.min(volume * 2, 1)}
 *         {@const color = `hsl(${200 + i * 40}, 80%, ${50 + intensity * 30}%)`}
 *         <div
 *           class="bar"
 *           style="height: {volume * 100}%; background: {color}"
 *         ></div>
 *       {/each}
 *     </div>
 *   {/snippet}
 * </AudioVisualizer.Root>
 * ```
 *
 * ### With Average Volume Display
 *
 * ```svelte
 * <AudioVisualizer.Root bands={5}>
 *   {#snippet children({ volumes, avgVolume, maxVolume })}
 *     <div class="visualizer-container">
 *       <div class="info">
 *         <span>Avg: {(avgVolume * 100).toFixed(0)}%</span>
 *         <span>Max: {(maxVolume * 100).toFixed(0)}%</span>
 *       </div>
 *
 *       <div class="visualizer">
 *         {#each volumes as volume}
 *           <div class="bar" style="height: {volume * 100}%"></div>
 *         {/each}
 *       </div>
 *     </div>
 *   {/snippet}
 * </AudioVisualizer.Root>
 * ```
 *
 * ### Starting/Stopping Visualization
 *
 * ```svelte
 * <script>
 *   import * as AudioVisualizer from '@hpd/hpd-agent-headless-ui/audio-visualizer';
 *
 *   let analyserNode: AnalyserNode | null = null;
 *   let visualizerState: any;
 *
 *   function startVisualizing(node: AnalyserNode) {
 *     analyserNode = node;
 *     visualizerState?.startVisualization(node);
 *   }
 *
 *   function stopVisualizing() {
 *     visualizerState?.stopVisualization();
 *     analyserNode = null;
 *   }
 * </script>
 *
 * <AudioVisualizer.Root>
 *   {#snippet children(state)}
 *     {@const _ = visualizerState || (visualizerState = state)}
 *
 *     {#if state.isActive}
 *       <button onclick={stopVisualizing}>Stop</button>
 *     {:else}
 *       <button onclick={() => startVisualizing(myAnalyserNode)}>Start</button>
 *     {/if}
 *
 *     <div class="visualizer">
 *       {#each state.volumes as volume}
 *         <div class="bar" style="height: {volume * 100}%"></div>
 *       {/each}
 *     </div>
 *   {/snippet}
 * </AudioVisualizer.Root>
 * ```
 *
 * ## Props
 *
 * - `bands?: number` - Number of frequency bands (default: 5, range: 1-32)
 * - `mode?: 'bar' | 'waveform' | 'radial'` - Visualization mode (default: 'bar')
 * - `onVolumesChange?: (volumes: number[]) => void` - Called when volumes update
 *
 * ## Snippet Props
 *
 * - `volumes: number[]` - Array of volume levels (0-1) for each band
 * - `bands: number` - Number of frequency bands
 * - `mode: 'bar' | 'waveform' | 'radial'` - Current visualization mode
 * - `maxVolume: number` - Maximum volume across all bands
 * - `avgVolume: number` - Average volume across all bands
 * - `isActive: boolean` - Whether visualization is currently running
 *
 * ## Data Attributes
 *
 * - `data-audio-visualizer-root` - Present on root element
 * - `data-mode` - Current mode (bar/waveform/radial)
 * - `data-bands` - Number of bands
 * - `data-active` - Present when visualization is running
 *
 * ## Accessibility
 *
 * - `role="img"` - Identifies as an image (decorative visualization)
 * - `aria-label="Audio visualization"` - Describes the visualization
 *
 * ## Methods (via state reference)
 *
 * - `startVisualization(analyserNode: AnalyserNode)` - Begin visualization loop
 * - `stopVisualization()` - Stop visualization and reset volumes
 * - `setBands(bands: number)` - Update number of frequency bands
 * - `setMode(mode: 'bar' | 'waveform' | 'radial')` - Change visualization mode
 *
 * ## Technical Details
 *
 * ### Frequency Analysis ( Pattern)
 *
 * - Uses Web Audio API `AnalyserNode`
 * - FFT size: 256 (fast, sufficient for 5-7 bands)
 * - Frequency data: `getByteFrequencyData()` (0-255 range)
 * - Band calculation: Split frequency bins evenly across bands
 * - Normalization: Divide by 255 to get 0-1 range
 *
 * ### Performance
 *
 * - Animation: `requestAnimationFrame` (60fps target)
 * - Memory: Reuses typed arrays (no allocations per frame)
 * - Cleanup: Automatic via Svelte 5 `$effect` cleanup
 *
 * @module AudioVisualizer
 */

export * from './exports.ts';
