/**
 * Input State Management
 *
 * AI-aware text input with auto-resize and form integration.
 */

import type { Box } from 'svelte-toolbelt';
import type { InputState, InputSnippetProps } from './types.ts';
import { kbd } from '$lib/internal/kbd.js';

export interface InputStateOptions {
	id: Box<string>;
	ref: Box<HTMLTextAreaElement | null>;
	value: Box<string>;
	disabled: Box<boolean>;
	maxRows: Box<number>;
	placeholder: Box<string>;
	autoFocus: Box<boolean>;
	name: Box<string | undefined>;
	required: Box<boolean>;
	ariaLabel: Box<string>;
}

export class InputStateClass {
	id: InputStateOptions['id'];
	ref: InputStateOptions['ref'];
	#value: InputStateOptions['value'];
	#disabled: InputStateOptions['disabled'];
	#maxRows: InputStateOptions['maxRows'];
	#placeholder: InputStateOptions['placeholder'];
	#autoFocus: InputStateOptions['autoFocus'];
	#name: InputStateOptions['name'];
	#required: InputStateOptions['required'];
	#ariaLabel: InputStateOptions['ariaLabel'];

	// Internal state
	#focused = $state(false);
	#rows = $state(1);

	// Reusable clone for measurement (avoids creating/destroying on every input)
	#measurementClone: HTMLTextAreaElement | null = null;

	constructor(options: InputStateOptions) {
		this.id = options.id;
		this.ref = options.ref;
		this.#value = options.value;
		this.#disabled = options.disabled;
		this.#maxRows = options.maxRows;
		this.#placeholder = options.placeholder;
		this.#autoFocus = options.autoFocus;
		this.#name = options.name;
		this.#required = options.required;
		this.#ariaLabel = options.ariaLabel;
	}

	// ============================================
	// Computed State
	// ============================================

	get value(): string {
		return this.#value.current;
	}

	get disabled(): boolean {
		return this.#disabled.current;
	}

	get filled(): boolean {
		return this.value.length > 0;
	}

	get focused(): boolean {
		return this.#focused;
	}

	get rows(): number {
		return this.#rows;
	}

	get maxRows(): number {
		return this.#maxRows.current;
	}

	/** Current component state for debugging/inspection */
	get state(): InputState {
		return {
			value: this.value,
			disabled: this.disabled,
			filled: this.filled,
			focused: this.focused,
			rows: this.rows,
			maxRows: this.maxRows,
		};
	}

	/** Props to pass to snippets */
	get snippetProps(): InputSnippetProps {
		return {
			value: this.value,
			disabled: this.disabled,
			filled: this.filled,
			focused: this.focused,
			rows: this.rows,
		};
	}

	// ============================================
	// Props Generation
	// ============================================

	readonly props = $derived.by(() => {
		return {
			id: this.id.current,
			role: 'textbox',
			'aria-label': this.#ariaLabel.current,
			'aria-multiline': 'true',
			'aria-disabled': this.disabled,
			'data-input': '',
			'data-disabled': this.disabled ? '' : undefined,
			'data-filled': this.filled ? '' : undefined,
			'data-focused': this.focused ? '' : undefined,
			'data-rows': this.rows.toString(),
			disabled: this.disabled,
			placeholder: this.#placeholder.current,
			autofocus: this.#autoFocus.current,
			rows: this.rows,
			name: this.#name.current,
			required: this.#required.current,
		} as const;
	});

	// ============================================
	// Event Handlers
	// ============================================

	handleInput = (event: Event & { currentTarget: HTMLTextAreaElement }) => {
		const textarea = event.currentTarget;
		// Update value through the setter provided in boxWith
		this.setValue(textarea.value);

		// Auto-resize logic
		// Note: This is also called by $effect in the component when value changes
		// We accept this double measurement for simplicity, as the performance impact is negligible
		// and ensuring synchronous updates is more important than micro-optimizing layout calls
		this.updateRows(textarea);
	};

	/**
	 * Synchronize rows with current value.
	 * Call this after programmatic value changes or on mount with initial values.
	 */
	syncRows() {
		const textarea = this.ref.current;
		if (textarea) {
			this.updateRows(textarea);
		}
	}

	/** Set the input value (uses the box setter) */
	setValue(newValue: string) {
		// boxWith creates a writable box when both getter and setter are provided
		// Check if the box has a setter by attempting to assign
		try {
			const valueBox = this.#value as { current: string };
			valueBox.current = newValue;
		} catch (error) {
			console.warn('[Input] setValue failed - box may be read-only:', error);
		}
	}

	handleFocus = () => {
		this.#focused = true;
	};

	handleBlur = () => {
		this.#focused = false;
	};

	handleKeyDown = (event: KeyboardEvent) => {
		// Submit on Enter (unless Shift is held for newline or IME is composing)
		// IME composition check prevents false submits when confirming Asian characters
		if (event.key === kbd.ENTER && !event.shiftKey && !event.isComposing) {
			return true; // Signal to component to handle submit
		}
		return false;
	};

	// ============================================
	// Auto-resize Logic
	// ============================================

	/**
	 * Updates the number of rows based on content height.
	 * Uses a hidden clone element to measure without visual flash.
	 *
	 */
	updateRows(textarea: HTMLTextAreaElement) {
		// For empty textarea, use 1 row
		if (this.value === '') {
			this.#rows = 1;
			return;
		}

		// Get or create reusable clone for measurement (reduces DOM churn)
		const clone = this.getMeasurementClone(textarea);

		// Measure content height in clone
		const computedStyle = getComputedStyle(clone);
		let lineHeight = parseFloat(computedStyle.lineHeight);

		// Fallback if line-height is "normal" or otherwise non-numeric
		if (!isFinite(lineHeight)) {
			const fontSize = parseFloat(computedStyle.fontSize);
			lineHeight = fontSize * 1.2; // Typical "normal" line-height multiplier
		}

		const paddingTop = parseFloat(computedStyle.paddingTop) || 0;
		const paddingBottom = parseFloat(computedStyle.paddingBottom) || 0;

		// scrollHeight includes padding, subtract to get content height
		const contentHeight = clone.scrollHeight - paddingTop - paddingBottom;

		// Note: We don't remove the clone - it's reused for performance

		// Calculate required rows based on content height
		const requiredRows = Math.max(1, Math.ceil(contentHeight / lineHeight));

		// Clamp between 1 and maxRows
		const newRows = Math.min(Math.max(1, requiredRows), this.maxRows);

		// Only update if changed (prevent unnecessary re-renders)
		if (!isNaN(newRows) && newRows !== this.#rows) {
			this.#rows = newRows;
		}
	}

	/**
	 * Gets or creates a reusable hidden clone for measurement.
	 * Reusing the clone reduces DOM churn and GC pressure.
	 */
	private getMeasurementClone(textarea: HTMLTextAreaElement): HTMLTextAreaElement {
		// Create clone on first use
		if (!this.#measurementClone) {
			this.#measurementClone = textarea.cloneNode() as HTMLTextAreaElement;
			this.#measurementClone.setAttribute('aria-hidden', 'true');
			// Remove any test IDs or data attributes that could cause conflicts
			this.#measurementClone.removeAttribute('data-testid');
			this.#measurementClone.removeAttribute('id');
			// Remove form-related attributes to avoid duplicate form submissions
			this.#measurementClone.removeAttribute('name');
			this.#measurementClone.removeAttribute('form');
			document.body.appendChild(this.#measurementClone);
		}

		const clone = this.#measurementClone;
		const computedStyle = getComputedStyle(textarea);

		// Update critical styles that affect measurement
		clone.style.cssText = `
			position: absolute !important;
			visibility: hidden !important;
			pointer-events: none !important;
			top: -9999px !important;
			left: -9999px !important;
			width: ${textarea.clientWidth}px !important;
			height: auto !important;
			font: ${computedStyle.font} !important;
			font-family: ${computedStyle.fontFamily} !important;
			font-size: ${computedStyle.fontSize} !important;
			font-weight: ${computedStyle.fontWeight} !important;
			line-height: ${computedStyle.lineHeight} !important;
			letter-spacing: ${computedStyle.letterSpacing} !important;
			padding: ${computedStyle.padding} !important;
			border: ${computedStyle.border} !important;
			box-sizing: ${computedStyle.boxSizing} !important;
			white-space: ${computedStyle.whiteSpace} !important;
			overflow-wrap: ${computedStyle.overflowWrap} !important;
		`;

		// Update value and rows for measurement
		clone.rows = 1;
		clone.value = this.value;

		return clone;
	}

	/**
	 * Clean up the reusable clone when component is destroyed.
	 */
	destroy() {
		if (this.#measurementClone) {
			this.#measurementClone.remove();
			this.#measurementClone = null;
		}
	}

	/** Clear the input value */
	clear() {
		this.setValue('');
		this.#rows = 1;
	}

	/** Focus the textarea */
	focus() {
		this.ref.current?.focus();
	}

	/** Blur the textarea */
	blur() {
		this.ref.current?.blur();
	}
}

export { InputStateClass as InputState };
