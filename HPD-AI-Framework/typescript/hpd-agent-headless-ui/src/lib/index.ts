/**
 * HPD Agent Headless UI - Main Entry Point
 *
 * Phase 2: Core AI components
 */

// Agent state and utilities (includes Message type)
export * from './agent/index.js';

// Workspace (V3 - Unified session/branch/streaming factory)
export { createWorkspace } from './workspace/index.js';
export type { Workspace, CreateWorkspaceOptions, SendOptions } from './workspace/index.js';

// RunConfig headless components
export * as RunConfig from './run-config/index.js';
export { RunConfigState } from './run-config/index.js';

// FileAttachment headless component
export * as FileAttachment from './file-attachment/index.js';
export { FileAttachmentState } from './file-attachment/index.js';

// Re-exports from hpd-agent-client — users only need to import from this package
export type {
	Session,
	Branch,
	BranchMessage,
	ContentReference,
	ChatRunConfig,
	CreateSessionRequest,
	CreateBranchRequest,
	ForkBranchRequest,
	ClientHarnessDefinition,
	AgentClientInput,
	ClientToolDefinition,
	ClientSkillDefinition,
	ClientToolInvokeResponse,
	ClientToolInvokeRequestEvent,
	PermissionChoice,
	AgentRunInputEvent,
	AgentEvent,
	// Agent definition types
	AgentSummaryDto,
	StoredAgentDto,
	CreateAgentRequest,
	UpdateAgentRequest,
	AgentConfig,
	// Eval types
	ScoreRecord,
	EvaluatorSummary,
	RiskAutonomyDataPoint,
	ScoreTrend,
	ScoreBucket,
	ScoreAggregate,
	PassRateResult,
	FailureRateResult,
	AgentComparisonResult,
	BranchComparisonResult,
	ToolUsageSummary,
	CostBreakdown,
} from '@hpd/hpd-agent-client';
export {
	createSuccessResponse,
	createErrorResponse,
	createExpandedHarness,
} from '@hpd/hpd-agent-client';

// BranchSwitcher component (V3 - Sibling navigation UI)
export * as BranchSwitcher from './branch-switcher/index.js';
export {
	BranchSwitcherRootState,
	BranchSwitcherPrevState,
	BranchSwitcherNextState,
	BranchSwitcherPositionState,
	branchSwitcherAttrs,
} from './branch-switcher/index.js';

// SessionList component (V3 - Session management UI)
export * as SessionList from './session-list/index.js';
export {
	SessionListRootState,
	SessionListItemState,
	SessionListEmptyState,
	SessionListCreateButtonState,
	sessionListAttrs,
} from './session-list/index.js';
export type {
	SessionListRootProps,
	SessionListItemProps,
	SessionListEmptyProps,
	SessionListCreateButtonProps,
	SessionListRootSnippetProps,
	SessionListItemSnippetProps,
} from './session-list/index.js';

// Message component (explicit exports to avoid conflicts)
export { Message, MessageState, createMessageState } from './message/index.js';
export type { MessageProps, MessageHTMLProps, MessageSnippetProps } from './message/index.js';

// MessageActions component (edit + retry buttons)
export * as MessageActions from './message-actions/index.js';
export {
	MessageActionsRootState,
	MessageActionsEditButtonState,
	MessageActionsRetryButtonState,
	MessageActionsCopyButtonState,
	MessageActionsPrevState,
	MessageActionsNextState,
	MessageActionsPositionState,
	messageActionsAttrs,
} from './message-actions/index.js';
export type {
	MessageActionsRootProps,
	MessageActionsEditButtonProps,
	MessageActionsRetryButtonProps,
	MessageActionsCopyButtonProps,
	MessageActionsPrevProps,
	MessageActionsPrevSnippetProps,
	MessageActionsNextProps,
	MessageActionsNextSnippetProps,
	MessageActionsPositionProps,
	MessageActionsRootSnippetProps,
	MessageActionsEditButtonSnippetProps,
	MessageActionsRetryButtonSnippetProps,
	MessageActionsCopyButtonSnippetProps,
	MessageActionsPositionSnippetProps,
	MessageActionStatus,
} from './message-actions/index.js';

// MessageEdit component (inline message editing)
export * as MessageEdit from './message-edit/index.js';
export {
	MessageEditRootState,
	MessageEditTextareaState,
	MessageEditSaveButtonState,
	MessageEditCancelButtonState,
	messageEditAttrs,
} from './message-edit/index.js';
export type {
	MessageEditRootProps,
	MessageEditRootHTMLProps,
	MessageEditRootSnippetProps,
	MessageEditTextareaProps,
	MessageEditTextareaSnippetProps,
	MessageEditSaveButtonProps,
	MessageEditSaveButtonSnippetProps,
	MessageEditCancelButtonProps,
	MessageEditCancelButtonSnippetProps,
} from './message-edit/index.js';

// MessageList component
export * as MessageList from './message-list/index.js';
export { MessageListState } from './message-list/index.js';
export type { MessageListProps, MessageListSnippetProps } from './message-list/index.js';

// Input component
export * as Input from './input/index.js';

// ChatInput component (compositional input with accessories)
export * as ChatInput from './chat-input/index.js';
export { ChatInputRootState } from './chat-input/index.js';
export type {ChatInputRootProps, ChatInputInputProps, ChatInputLeadingProps, ChatInputTrailingProps,
	ChatInputTopProps, ChatInputBottomProps
} from './chat-input/index.js';

// ToolExecution component
export { ToolExecution } from './tool-execution/index.js';

// PermissionDialog component
export * as PermissionDialog from './permission-dialog/index.js';

// Audio components (Phase 3A)
export * as AudioPlaybackGate from './audio-playback-gate/index.js';
export * as AudioPlayer from './audio-player/index.js';
export * as Transcription from './transcription/index.js';
export * as VoiceActivityIndicator from './voice-activity-indicator/index.js';

// Audio components (Phase 3B)
export * as InterruptionIndicator from './interruption-indicator/index.js';
export * as TurnIndicator from './turn-indicator/index.js';
export * as AudioVisualizer from './audio-visualizer/index.js';

// Testing utilities (mock workspace)
export { createMockWorkspace } from './testing/mock-agent.svelte.js';
export type { MockWorkspaceOptions } from './testing/mock-agent.svelte.js';

// ========================================
// Storage System
// ========================================
export * from './storage/index.js';

// ========================================
// SplitPanel Component
// ========================================
export * as SplitPanel from './split-panel/index.js';

// ========================================
// Artifact Component
// ========================================
export * as Artifact from './artifact/index.js';
export { ArtifactProviderState, ArtifactRootState, ArtifactPanelState } from './artifact/index.js';
export type {
	ArtifactProviderProps,
	ArtifactRootProps,
	ArtifactSlotProps,
	ArtifactTriggerProps,
	ArtifactPanelProps,
	ArtifactTitleProps,
	ArtifactContentProps,
	ArtifactCloseProps,
	ArtifactPanelSnippetProps,
	ArtifactRootSnippetProps
} from './artifact/index.js';

// ========================================
// Utilities (for extending the library)
// ========================================

// Data attributes and styling
export {
	createHPDAttrs,
	boolToStr,
	boolToStrTrueOrUndef,
	boolToEmptyStrOrUndef,
	boolToTrueOrUndef,
	getDataOpenClosed,
	getDataChecked,
	getAriaChecked
} from './internal/attrs.js';
export type { CreateHPDAttrsReturn } from './internal/attrs.js';

// Keyboard constants
export { kbd } from './internal/kbd.js';
export type { KbdKey } from './internal/kbd.js';

// Common utilities
export { noop } from './internal/noop.js';
export { createId } from './internal/create-id.js';
export { debounce } from './internal/debounce.js';

// Type utilities
export type {
	WithChild,
	Without,
	OnChangeFn,
	HPDKeyboardEvent,
	HPDMouseEvent,
	WithRefOpts,
	RefAttachment
} from './internal/types.js';

// Focus management
export { RovingFocusGroup } from './internal/roving-focus-group.js';
export {
	focus,
	focusFirst,
	focusWithoutScroll,
	getTabbableCandidates,
	getTabbableEdges,
	findVisible,
	handleCalendarInitialFocus
} from './internal/focus.js';
export type { FocusableTarget } from './internal/focus.js';

export { getTabbableFrom, getTabbableFromFocusable, isTabbable, isFocusable, tabbable, focusable } from './internal/tabbable.js';

// Resize observer
export { HPDResizeObserver } from './internal/svelte-resize-observer.svelte.js';

// Animation utilities
export { PresenceManager } from './internal/presence-manager.svelte.js';
export { AnimationsComplete } from './internal/animations-complete.js';

// DOM utilities
export { getFirstNonCommentChild, isClickTrulyOutside } from './internal/dom.js';

// Event utilities
export { CustomEventDispatcher } from './internal/events.js';
export type { EventCallback } from './internal/events.js';

// Locale and direction
export { getElemDirection } from './internal/locale.js';
export type { Direction } from './internal/locale.js';

// Directional keys
export {
	getNextKey,
	getPrevKey,
	getDirectionalKeys,
	FIRST_KEYS,
	LAST_KEYS,
	FIRST_LAST_KEYS,
	SELECTION_KEYS
} from './internal/get-directional-keys.js';
export type { Orientation } from './internal/get-directional-keys.js';

// Type checking utilities
export {
	isBrowser,
	isIOS,
	isFunction,
	isHTMLElement,
	isElement,
	isElementOrSVGElement,
	isNumberString,
	isNull,
	isTouch,
	isFocusVisible,
	isNotNull,
	isSelectableInput,
	isElementHidden
} from './internal/is.js';
