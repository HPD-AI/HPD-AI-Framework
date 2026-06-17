/**
 * ChatInput State - Reactive State Manager for Compositional Chat Input
 *
 * This component provides shared state and behavior for a chat input with accessories.
 * It follows the MessageList pattern - a container that provides context for child components.
 *
 * Key Features:
 * - Shared state across all child components (Input, Leading, Trailing, etc.)
 * - Value management (controlled and uncontrolled)
 * - Submit handling with validation
 * - Focus management
 * - Character count tracking
 *
 * Architecture:
 * - ChatInput.Root provides context
 * - ChatInput.Input/Leading/Trailing/Top/Bottom consume context
 * - All state managed via Svelte 5 runes
 */

import { boxWith, type ReadableBoxedValues } from 'svelte-toolbelt';
import { Context } from 'runed';
import { createHPDAttrs } from '$lib/internal/attrs.js';
import type { OnChangeFn } from '$lib/internal/types.js';

// ============================================
// Data Attributes for CSS Hooks
// ============================================

const chatInputAttrs = createHPDAttrs({
	component: 'chat-input',
	parts: ['root', 'top', 'leading', 'input', 'trailing', 'bottom']
});

// ============================================
// Context
// ============================================

const ChatInputRootContext = new Context<ChatInputRootState>('ChatInput.Root');

// ============================================
// Root State (Manages Shared Input State)
// ============================================

interface ChatInputRootStateOpts
	extends ReadableBoxedValues<{
		value?: string;
		defaultValue?: string;
		disabled?: boolean;
		onSubmit?: (details: { value: string }) => void;
		onChange?: OnChangeFn<string>;
	}> {}

export class ChatInputRootState {
	static create(opts: ChatInputRootStateOpts) {
		return ChatInputRootContext.set(new ChatInputRootState(opts));
	}

	static get() {
		return ChatInputRootContext.get();
	}

	readonly opts: ChatInputRootStateOpts;

	// Internal state
	#internalValue = $state('');
	#focused = $state(false);

	// Whether value is controlled by parent
	#isControlled = $derived.by(() => this.opts.value?.current !== undefined);

	// Current value (controlled or uncontrolled)
	readonly value = $derived.by(() => {
		if (this.#isControlled) {
			const val = this.opts.value!.current;
			return typeof val === 'string' ? val : '';
		}
		return this.#internalValue;
	});

	// Disabled state
	readonly disabled = $derived.by(() => this.opts.disabled?.current ?? false);

	// Derived state
	readonly focused = $derived(this.#focused);
	readonly characterCount = $derived(this.value.length);
	readonly isEmpty = $derived(this.value.trim() === '');
	readonly canSubmit = $derived(!this.isEmpty && !this.disabled);

	constructor(opts: ChatInputRootStateOpts) {
		this.opts = opts;

		// Initialize internal value from defaultValue
		const defaultValue = opts.defaultValue?.current ?? '';
		this.#internalValue = defaultValue;
	}

	// Update value (for uncontrolled mode)
	updateValue(newValue: string, reason: 'user' | 'programmatic' = 'user') {
		if (!this.#isControlled) {
			this.#internalValue = newValue;
		}

		// Call onChange callback
		const onChange = this.opts.onChange?.current;
		if (onChange) {
			onChange(newValue);
		}
	}

	// Submit handler
	submit() {
		if (!this.canSubmit) return;

		const onSubmit = this.opts.onSubmit?.current;
		if (onSubmit) {
			onSubmit({ value: this.value });
		}
	}

	// Clear input
	clear() {
		this.updateValue('', 'programmatic');
	}

	// Focus management
	setFocused(focused: boolean) {
		this.#focused = focused;
	}

	// Get HPD attribute for a part
	getHPDAttr: typeof chatInputAttrs.getAttr = (part) => {
		return chatInputAttrs.getAttr(part);
	};

	// Shared props for all child components
	readonly sharedProps = $derived.by(
		() =>
			({
				'data-disabled': this.disabled ? '' : undefined,
				'data-focused': this.focused ? '' : undefined,
				'data-empty': this.isEmpty ? '' : undefined
			}) as const
	);
}
