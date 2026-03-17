<script lang="ts">
	import { mergeProps } from 'svelte-toolbelt';
	import { createId } from '$lib/internal/create-id.js';
	import type { InputProps } from '../types.js';
	import { kbd } from '$lib/internal/kbd.js';

	const uid = $props.id();

	let {
		id = createId(uid),
		ref = $bindable(null),
		value = $bindable(),
		defaultValue = '',
		onChange,
		onSubmit,
		disabled = false,
		placeholder = 'Type a message...',
		autoFocus = false,
		autoResize = false,
		name,
		required = false,
		maxRows = 10,
		'aria-label': ariaLabel = 'Message input',
		child,
		class: className,
		...restProps
	}: InputProps = $props();

	// Determine if controlled mode
	const isControlled = value !== undefined;
	// svelte-ignore state_referenced_locally
	let internalValue = $state(defaultValue ?? '');

	// Sync internal value with controlled value
	$effect(() => {
		if (isControlled && value !== undefined) {
			internalValue = value;
		}
	});

	// Resolved value (controlled takes precedence)
	const resolvedValue = $derived(isControlled ? (value ?? '') : internalValue);

	// Auto-resize state
	let rows = $state(1);
	let measurementClone: HTMLTextAreaElement | null = null;

	// Auto-resize logic
	function updateRows(textarea: HTMLTextAreaElement) {
		// For empty textarea, use 1 row
		if (resolvedValue === '') {
			rows = 1;
			return;
		}

		// Get or create reusable clone for measurement
		if (!measurementClone) {
			measurementClone = textarea.cloneNode() as HTMLTextAreaElement;
			measurementClone.setAttribute('aria-hidden', 'true');
			measurementClone.removeAttribute('data-testid');
			measurementClone.removeAttribute('id');
			measurementClone.removeAttribute('name');
			measurementClone.removeAttribute('form');
			document.body.appendChild(measurementClone);
		}

		const clone = measurementClone;
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

		clone.rows = 1;
		clone.value = resolvedValue;

		// Measure content height
		let lineHeight = parseFloat(computedStyle.lineHeight);
		if (!isFinite(lineHeight)) {
			const fontSize = parseFloat(computedStyle.fontSize);
			lineHeight = fontSize * 1.2;
		}

		const paddingTop = parseFloat(computedStyle.paddingTop) || 0;
		const paddingBottom = parseFloat(computedStyle.paddingBottom) || 0;
		const contentHeight = clone.scrollHeight - paddingTop - paddingBottom;

		// Calculate required rows
		const requiredRows = Math.max(1, Math.ceil(contentHeight / lineHeight));
		const newRows = Math.min(Math.max(1, requiredRows), maxRows);

		if (!isNaN(newRows) && newRows !== rows) {
			rows = newRows;
		}
	}

	// Sync rows when value changes
	$effect(() => {
		if (autoResize && ref) {
			updateRows(ref);
		}
	});

	// Cleanup
	$effect(() => {
		return () => {
			if (measurementClone) {
				measurementClone.remove();
				measurementClone = null;
			}
		};
	});

	// Event handlers
	function handleInput(event: Event & { currentTarget: HTMLTextAreaElement }) {
		const textarea = event.currentTarget;
		const newValue = textarea.value;

		// Update value
		if (isControlled) {
			value = newValue;
		} else {
			internalValue = newValue;
		}

		// Auto-resize
		updateRows(textarea);

		// Trigger onChange callback
		onChange?.({
			reason: 'input-change',
			event,
			value: newValue,
		});
	}

	function handleKeyDown(event: KeyboardEvent) {
		// Submit on Enter (unless Shift is held for newline)
		if (event.key === kbd.ENTER && !event.shiftKey && !event.isComposing) {
			event.preventDefault();

			// Only submit if value is not empty (trimmed)
			const trimmedValue = resolvedValue.trim();
			if (trimmedValue && onSubmit) {
				onSubmit({
					value: trimmedValue,
					event,
				});
			}
		}
	}

	let focused = $state(false);

	function handleFocus() {
		focused = true;
	}

	function handleBlur() {
		focused = false;
	}

	// Merge props
	const mergedProps = $derived(
		mergeProps(restProps, {
			id,
			role: 'textbox',
			'aria-label': ariaLabel,
			'aria-multiline': 'true',
			'aria-disabled': disabled,
			'data-input': '',
			'data-disabled': disabled ? '' : undefined,
			'data-filled': resolvedValue.length > 0 ? '' : undefined,
			'data-focused': focused ? '' : undefined,
			'data-rows': autoResize ? rows.toString() : undefined,
			class: className,
			disabled,
			placeholder,
			autofocus: autoFocus,
			rows: autoResize ? rows : undefined,
			name,
			required,
			value: resolvedValue,
			oninput: handleInput,
			onfocus: handleFocus,
			onblur: handleBlur,
			onkeydown: handleKeyDown,
		})
	);
</script>

{#if child}
	{@render child({ props: mergedProps })}
{:else}
	<textarea bind:this={ref} {...mergedProps}></textarea>
{/if}
