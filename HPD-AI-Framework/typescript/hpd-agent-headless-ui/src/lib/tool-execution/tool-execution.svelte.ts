/**
 * ToolExecution State Management
 *
 * Reactive state classes for tool execution visualization.
 */

import { Context } from 'runed';
import {
	boxWith,
	type ReadableBoxedValues,
	type WritableBoxedValues
} from 'svelte-toolbelt';
import { kbd } from '$lib/internal/kbd.js';
import type { ToolCall, ToolCallStatus } from '../agent/types.ts';
import type {
	ToolExecutionExpandReason,
	ToolExecutionExpandEventDetails,
	ToolExecutionRootHTMLProps,
	ToolExecutionRootSnippetProps,
	ToolExecutionTriggerHTMLProps,
	ToolExecutionTriggerSnippetProps,
	ToolExecutionContentHTMLProps,
	ToolExecutionContentSnippetProps,
	ToolExecutionStatusHTMLProps,
	ToolExecutionStatusSnippetProps,
	ToolExecutionArgsHTMLProps,
	ToolExecutionArgsSnippetProps,
	ToolExecutionResultHTMLProps,
	ToolExecutionResultSnippetProps
} from './types.ts';

// ============================================
// Root Context
// ============================================

const ToolExecutionRootContext = new Context<ToolExecutionRootState>('ToolExecution.Root');

// ============================================
// Root State Options
// ============================================

interface ToolExecutionRootStateOpts
	extends WritableBoxedValues<{
			expanded: boolean;
		}>,
		ReadableBoxedValues<{
			onExpandChange:
				| ((expanded: boolean, details: ToolExecutionExpandEventDetails) => void)
				| undefined;
		}> {
	toolCall: ToolCall;
}

// ============================================
// Root State Class
// ============================================

export class ToolExecutionRootState {
	// ============================================
	// Props (Immutable)
	// ============================================

	readonly callId: string;
	readonly name: string;
	readonly messageId: string;
	readonly opts: ToolExecutionRootStateOpts;

	// ============================================
	// Reactive State ($state runes)
	// ============================================

	status = $state<ToolCallStatus>('pending');
	args = $state<Record<string, unknown>>({});
	result = $state<string | undefined>(undefined);
	error = $state<string | undefined>(undefined);
	startTime = $state<Date>(new Date());
	endTime = $state<Date | undefined>(undefined);

	// ============================================
	// Derived State ($derived)
	// ============================================

	/**
	 * Duration of tool execution in milliseconds
	 */
	readonly duration = $derived(
		this.endTime ? this.endTime.getTime() - this.startTime.getTime() : undefined
	);

	/**
	 * Whether the tool is actively executing
	 */
	readonly isActive = $derived(this.status === 'pending' || this.status === 'executing');

	/**
	 * Whether the tool execution is complete
	 */
	readonly isComplete = $derived(this.status === 'complete');

	/**
	 * Whether the tool execution resulted in an error
	 */
	readonly hasError = $derived(this.status === 'error');

	/**
	 * Whether the tool has a result
	 */
	readonly hasResult = $derived(this.result !== undefined && !this.hasError);

	/**
	 * Whether the tool has arguments
	 */
	readonly hasArgs = $derived(Object.keys(this.args).length > 0);

	/**
	 * HTML props for the root element (data attributes + ARIA)
	 */
	get props(): ToolExecutionRootHTMLProps {
		return {
			'data-tool-execution-root': '',
			'data-tool-id': this.callId,
			'data-tool-name': this.name,
			'data-tool-status': this.status,
			'data-expanded': this.opts.expanded.current ? '' : undefined,
			'data-active': this.isActive ? '' : undefined,
			'data-complete': this.isComplete ? '' : undefined,
			'data-error': this.hasError ? '' : undefined,
			role: 'status',
			'aria-label': `Tool: ${this.name}`,
			'aria-busy': this.isActive,
			'aria-live': 'polite'
		};
	}

	/**
	 * Snippet props for content customization
	 */
	get snippetProps(): ToolExecutionRootSnippetProps {
		return {
			name: this.name,
			status: this.status,
			expanded: this.opts.expanded.current,
			isActive: this.isActive,
			isComplete: this.isComplete,
			hasError: this.hasError,
			hasResult: this.hasResult,
			hasArgs: this.hasArgs,
			duration: this.duration,
			args: this.args,
			result: this.result,
			error: this.error
		};
	}

	// ============================================
	// Constructor
	// ============================================

	constructor(opts: ToolExecutionRootStateOpts) {
		this.opts = opts;
		const { toolCall } = opts;

		this.callId = toolCall.callId;
		this.name = toolCall.name;
		this.messageId = toolCall.messageId;
		this.status = toolCall.status;
		this.args = toolCall.args ?? {};
		this.result = toolCall.result;
		this.error = toolCall.error;
		this.startTime = toolCall.startTime;
		this.endTime = toolCall.endTime;
	}

	// ============================================
	// Sets context automatically
	// ============================================

	static create(opts: ToolExecutionRootStateOpts): ToolExecutionRootState {
		return ToolExecutionRootContext.set(new ToolExecutionRootState(opts));
	}

	// ============================================
	// Methods 
	// ============================================

	/**
	 * Toggle the expanded state with event details and cancellation support
	 */
	toggleExpanded(
		reason: ToolExecutionExpandReason = 'trigger-press',
		event?: Event,
		trigger?: Element
	): void {
		const newExpanded = !this.opts.expanded.current;

		// Create event details with cancellation support
		let canceled = false;
		const details: ToolExecutionExpandEventDetails = {
			reason,
			event,
			trigger,
			cancel: () => {
				canceled = true;
			},
			get isCanceled() {
				return canceled;
			}
		};

		// Call user callback
		this.opts.onExpandChange.current?.(newExpanded, details);

		// Only toggle if not canceled
		if (!details.isCanceled) {
			this.opts.expanded.current = newExpanded;
		}
	}

	/**
	 * Update the tool execution state from a ToolCall object
	 * Used when parent AgentState updates the tool call
	 */
	update(toolCall: ToolCall): void {
		this.status = toolCall.status;
		this.args = toolCall.args ?? {};
		this.result = toolCall.result;
		this.error = toolCall.error;
		this.endTime = toolCall.endTime;
	}
}

// ============================================
// Trigger State Class
// ============================================

export class ToolExecutionTriggerState {
	readonly root: ToolExecutionRootState;

	constructor(root: ToolExecutionRootState) {
		this.root = root;
	}

	static create(): ToolExecutionTriggerState {
		const root = ToolExecutionRootContext.get();
		return new ToolExecutionTriggerState(root);
	}

	/**
	 * HTML props for the trigger element
	 */
	get props(): ToolExecutionTriggerHTMLProps {
		return {
			'data-tool-execution-trigger': '',
			'data-expanded': this.root.opts.expanded.current ? '' : undefined,
			type: 'button',
			'aria-expanded': this.root.opts.expanded.current,
			'aria-controls': `tool-content-${this.root.callId}`,
			onclick: (e: MouseEvent) => {
				this.root.toggleExpanded('trigger-press', e, e.currentTarget as Element);
			},
			onkeydown: (e: KeyboardEvent) => {
				if (e.key === kbd.ENTER || e.key === kbd.SPACE) {
					e.preventDefault();
					this.root.toggleExpanded('keyboard', e, e.currentTarget as Element);
				}
			}
		};
	}

	/**
	 * Snippet props for trigger customization
	 */
	get snippetProps(): ToolExecutionTriggerSnippetProps {
		return {
			expanded: this.root.opts.expanded.current,
			name: this.root.name,
			status: this.root.status
		};
	}
}

// ============================================
// Content State Class
// ============================================

export class ToolExecutionContentState {
	readonly root: ToolExecutionRootState;

	constructor(root: ToolExecutionRootState) {
		this.root = root;
	}

	static create(): ToolExecutionContentState {
		const root = ToolExecutionRootContext.get();
		return new ToolExecutionContentState(root);
	}

	/**
	 * Whether the content should be rendered
	 * For now, always render (we can add animation support later)
	 */
	readonly shouldRender = $derived(true);

	/**
	 * HTML props for the content element
	 */
	get props(): ToolExecutionContentHTMLProps {
		return {
			'data-tool-execution-content': '',
			'data-expanded': this.root.opts.expanded.current ? '' : undefined,
			id: `tool-content-${this.root.callId}`,
			'aria-labelledby': `tool-trigger-${this.root.callId}`
		};
	}

	/**
	 * Snippet props for content customization
	 */
	get snippetProps(): ToolExecutionContentSnippetProps {
		return {
			expanded: this.root.opts.expanded.current,
			hasArgs: this.root.hasArgs,
			hasResult: this.root.hasResult,
			hasError: this.root.hasError
		};
	}
}

// ============================================
// Status State Class
// ============================================

export class ToolExecutionStatusState {
	readonly root: ToolExecutionRootState;

	constructor(root: ToolExecutionRootState) {
		this.root = root;
	}

	static create(): ToolExecutionStatusState {
		const root = ToolExecutionRootContext.get();
		return new ToolExecutionStatusState(root);
	}

	/**
	 * HTML props for the status element
	 */
	get props(): ToolExecutionStatusHTMLProps {
		return {
			'data-tool-execution-status': '',
			'data-tool-status': this.root.status,
			'data-active': this.root.isActive ? '' : undefined,
			'data-complete': this.root.isComplete ? '' : undefined,
			'data-error': this.root.hasError ? '' : undefined
		};
	}

	/**
	 * Snippet props for status customization
	 */
	get snippetProps(): ToolExecutionStatusSnippetProps {
		return {
			status: this.root.status,
			isActive: this.root.isActive,
			isComplete: this.root.isComplete,
			hasError: this.root.hasError
		};
	}
}

// ============================================
// Args State Class
// ============================================

export class ToolExecutionArgsState {
	readonly root: ToolExecutionRootState;

	constructor(root: ToolExecutionRootState) {
		this.root = root;
	}

	static create(): ToolExecutionArgsState {
		const root = ToolExecutionRootContext.get();
		return new ToolExecutionArgsState(root);
	}

	/**
	 * JSON-formatted args string
	 */
	get argsJson(): string {
		return JSON.stringify(this.root.args, null, 2);
	}

	/**
	 * HTML props for the args element
	 */
	get props(): ToolExecutionArgsHTMLProps {
		return {
			'data-tool-execution-args': '',
			'data-has-args': this.root.hasArgs ? '' : undefined
		};
	}

	/**
	 * Snippet props for args customization
	 */
	get snippetProps(): ToolExecutionArgsSnippetProps {
		return {
			args: this.root.args,
			hasArgs: this.root.hasArgs,
			argsJson: this.argsJson
		};
	}
}

// ============================================
// Result State Class
// ============================================

export class ToolExecutionResultState {
	readonly root: ToolExecutionRootState;

	constructor(root: ToolExecutionRootState) {
		this.root = root;
	}

	static create(): ToolExecutionResultState {
		const root = ToolExecutionRootContext.get();
		return new ToolExecutionResultState(root);
	}

	/**
	 * HTML props for the result element
	 */
	get props(): ToolExecutionResultHTMLProps {
		return {
			'data-tool-execution-result': '',
			'data-has-result': this.root.hasResult ? '' : undefined,
			'data-has-error': this.root.hasError ? '' : undefined
		};
	}

	/**
	 * Snippet props for result customization
	 */
	get snippetProps(): ToolExecutionResultSnippetProps {
		return {
			result: this.root.result,
			error: this.root.error,
			hasResult: this.root.hasResult,
			hasError: this.root.hasError
		};
	}
}
