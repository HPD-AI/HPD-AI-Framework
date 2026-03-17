/**
 * TurnIndicator Component
 *
 * Provides visual feedback for whose turn it is to speak in a conversation.
 *
 * ## Features
 *
 * - **Turn detection** - Shows user turn, agent turn, or unknown
 * - **Completion probability** - Confidence level for turn detection
 * - **Detection method** - How turn was detected (ML, heuristic, manual, timeout)
 * - **Turn helpers** - Boolean flags for easy conditional rendering
 *
 * ## Usage
 *
 * ### Basic Example
 *
 * ```svelte
 * <script>
 *   import * as TurnIndicator from '@hpd/hpd-agent-headless-ui/turn-indicator';
 * </script>
 *
 * <TurnIndicator.Root>
 *   {#snippet children({ currentTurn, isUserTurn, isAgentTurn })}
 *     <div class="turn-indicator" data-turn={currentTurn}>
 *       {#if isUserTurn}
 *         <div class="user-turn">Your turn to speak</div>
 *       {:else if isAgentTurn}
 *         <div class="agent-turn">Agent is responding</div>
 *       {:else}
 *         <div class="unknown-turn">...</div>
 *       {/if}
 *     </div>
 *   {/snippet}
 * </TurnIndicator.Root>
 * ```
 *
 * ### With Completion Probability
 *
 * ```svelte
 * <TurnIndicator.Root>
 *   {#snippet children({ currentTurn, completionProbability })}
 *     <div class="turn-indicator">
 *       <div class="turn-badge" data-turn={currentTurn}>
 *         {currentTurn === 'user' ? '🎤 You' : '🤖 Agent'}
 *       </div>
 *       {#if completionProbability > 0}
 *         <div class="confidence">
 *           {(completionProbability * 100).toFixed(0)}% confident
 *         </div>
 *       {/if}
 *     </div>
 *   {/snippet}
 * </TurnIndicator.Root>
 * ```
 *
 * ### With Detection Method
 *
 * ```svelte
 * <TurnIndicator.Root>
 *   {#snippet children({ currentTurn, detectionMethod, completionProbability })}
 *     <div class="turn-indicator" data-method={detectionMethod}>
 *       <strong>{currentTurn === 'user' ? 'Your Turn' : 'Agent Turn'}</strong>
 *       {#if detectionMethod === 'ml'}
 *         <span class="method">ML-detected ({(completionProbability * 100).toFixed(0)}%)</span>
 *       {:else if detectionMethod === 'heuristic'}
 *         <span class="method">Heuristic-based</span>
 *       {:else if detectionMethod === 'timeout'}
 *         <span class="method">Timeout</span>
 *       {/if}
 *     </div>
 *   {/snippet}
 * </TurnIndicator.Root>
 * ```
 *
 * ### With Callback
 *
 * ```svelte
 * <script>
 *   function handleTurnChange(turn: 'user' | 'agent' | 'unknown') {
 *     console.log('Turn changed:', turn);
 *     if (turn === 'user') {
 *       // Enable microphone, show input prompt, etc.
 *     } else if (turn === 'agent') {
 *       // Mute microphone, show "listening" state, etc.
 *     }
 *   }
 * </script>
 *
 * <TurnIndicator.Root onTurnChange={handleTurnChange}>
 *   {#snippet children({ currentTurn })}
 *     <!-- Your UI -->
 *   {/snippet}
 * </TurnIndicator.Root>
 * ```
 *
 * ## Props
 *
 * - `onTurnChange?: (turn: 'user' | 'agent' | 'unknown') => void` - Called when turn changes
 *
 * ## Snippet Props
 *
 * - `currentTurn: 'user' | 'agent' | 'unknown'` - Current speaker turn
 * - `completionProbability: number` - Confidence level (0-1) from TURN_DETECTED event
 * - `detectionMethod: 'heuristic' | 'ml' | 'manual' | 'timeout' | null` - How turn was detected
 * - `isUserTurn: boolean` - Helper for currentTurn === 'user'
 * - `isAgentTurn: boolean` - Helper for currentTurn === 'agent'
 * - `isUnknown: boolean` - Helper for currentTurn === 'unknown'
 *
 * ## Data Attributes
 *
 * - `data-turn-indicator-root` - Present on root element
 * - `data-turn` - Current turn (user/agent/unknown)
 * - `data-user-turn` - Present when user's turn
 * - `data-agent-turn` - Present when agent's turn
 * - `data-unknown` - Present when turn is unknown
 *
 * ## Accessibility
 *
 * - `role="status"` - Announces changes to screen readers
 * - `aria-label="Current turn: [turn]"` - Describes whose turn it is
 * - `aria-live="polite"` - Announces turn changes politely (not assertive since it's informational)
 *
 * ## HPD Protocol Events
 *
 * This component responds to these HPD protocol events:
 *
 * - `TURN_DETECTED` - Turn change detected (sets completion probability and detection method)
 * - `VAD_START_OF_SPEECH` - User started speaking (sets turn to 'user')
 * - `SYNTHESIS_STARTED` - Agent started speaking (sets turn to 'agent')
 *
 * @module TurnIndicator
 */

export * from './exports.ts';
