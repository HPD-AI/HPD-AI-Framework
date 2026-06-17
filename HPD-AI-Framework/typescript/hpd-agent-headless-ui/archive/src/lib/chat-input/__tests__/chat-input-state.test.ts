/**
 * ChatInputRootState Tests - State Management Unit Tests
 *
 * Tests the core reactive state manager for the ChatInput component.
 * Note: These tests verify the state logic, but don't test Svelte reactivity
 * since that requires a Svelte runtime context (tested in browser tests).
 */

import { describe, it, expect, vi } from 'vitest';
import { ChatInputRootState } from '../chat-input.svelte.ts';
import { boxWith } from 'svelte-toolbelt';

describe('ChatInputRootState', () => {
	describe('Value Management', () => {
		it('should initialize with empty string in uncontrolled mode', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => undefined),
				disabled: boxWith(() => false)
			});

			expect(state.value).toBe('');
		});

		it('should initialize with defaultValue in uncontrolled mode', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => 'Hello World'),
				disabled: boxWith(() => false)
			});

			expect(state.value).toBe('Hello World');
		});

		it('should use controlled value when provided', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => 'Controlled Value'),
				defaultValue: boxWith(() => 'Default Value'),
				disabled: boxWith(() => false)
			});

			expect(state.value).toBe('Controlled Value');
		});

		it('should update internal value in uncontrolled mode', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => ''),
				disabled: boxWith(() => false)
			});

			state.updateValue('New value');
			expect(state.value).toBe('New value');
		});

		it('should NOT update internal value in controlled mode', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => 'Controlled'),
				defaultValue: boxWith(() => ''),
				disabled: boxWith(() => false)
			});

			// Attempt to update internal value (in controlled mode, this only triggers onChange)
			state.updateValue('Should not change');

			// Value should still be controlled value
			expect(state.value).toBe('Controlled');
		});

		it('should call onChange callback when value changes', () => {
			const onChange = vi.fn();
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => ''),
				disabled: boxWith(() => false),
				onChange: boxWith(() => onChange)
			});

			state.updateValue('New value');

			expect(onChange).toHaveBeenCalledWith('New value');
			expect(onChange).toHaveBeenCalledTimes(1);
		});

		it('should call onChange even in controlled mode', () => {
			const onChange = vi.fn();
			const state = new ChatInputRootState({
				value: boxWith(() => 'Controlled'),
				defaultValue: boxWith(() => ''),
				disabled: boxWith(() => false),
				onChange: boxWith(() => onChange)
			});

			state.updateValue('New value');

			expect(onChange).toHaveBeenCalledWith('New value');
		});
	});

	describe('Submit Handling', () => {
		it('should call onSubmit when submit() is called with valid input', () => {
			const onSubmit = vi.fn();
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => 'Hello'),
				disabled: boxWith(() => false),
				onSubmit: boxWith(() => onSubmit)
			});

			state.submit();

			expect(onSubmit).toHaveBeenCalledWith({ value: 'Hello' });
			expect(onSubmit).toHaveBeenCalledTimes(1);
		});

		it('should NOT submit when value is empty', () => {
			const onSubmit = vi.fn();
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => ''),
				disabled: boxWith(() => false),
				onSubmit: boxWith(() => onSubmit)
			});

			state.submit();

			expect(onSubmit).not.toHaveBeenCalled();
		});

		it('should NOT submit when value is only whitespace', () => {
			const onSubmit = vi.fn();
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => '   \n\t  '),
				disabled: boxWith(() => false),
				onSubmit: boxWith(() => onSubmit)
			});

			state.submit();

			expect(onSubmit).not.toHaveBeenCalled();
		});

		it('should NOT submit when disabled', () => {
			const onSubmit = vi.fn();
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => 'Hello'),
				disabled: boxWith(() => true),
				onSubmit: boxWith(() => onSubmit)
			});

			state.submit();

			expect(onSubmit).not.toHaveBeenCalled();
		});

		it('should submit trimmed whitespace as valid content', () => {
			const onSubmit = vi.fn();
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => '  Hello World  '),
				disabled: boxWith(() => false),
				onSubmit: boxWith(() => onSubmit)
			});

			state.submit();

			expect(onSubmit).toHaveBeenCalledWith({ value: '  Hello World  ' });
		});
	});

	describe('Derived State', () => {
		it('should calculate characterCount correctly for initial value', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => 'Hello'),
				disabled: boxWith(() => false)
			});

			expect(state.characterCount).toBe(5);
		});

		it('should detect isEmpty for empty string', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => ''),
				disabled: boxWith(() => false)
			});

			expect(state.isEmpty).toBe(true);
		});

		it('should detect isEmpty for whitespace-only string', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => '  \n\t  '),
				disabled: boxWith(() => false)
			});

			expect(state.isEmpty).toBe(true);
		});

		it('should detect isEmpty is false for content', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => 'Hello'),
				disabled: boxWith(() => false)
			});

			expect(state.isEmpty).toBe(false);
		});

		it('should calculate canSubmit when has content and not disabled', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => 'Hello'),
				disabled: boxWith(() => false)
			});

			expect(state.canSubmit).toBe(true);
		});

		it('should calculate canSubmit false when empty', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => ''),
				disabled: boxWith(() => false)
			});

			expect(state.canSubmit).toBe(false);
		});

		it('should calculate canSubmit false when disabled', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => 'Hello'),
				disabled: boxWith(() => true)
			});

			expect(state.canSubmit).toBe(false);
		});
	});

	describe('Focus Management', () => {
		it('should initialize with focused = false', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => ''),
				disabled: boxWith(() => false)
			});

			expect(state.focused).toBe(false);
		});

		it('should update focused state with setFocused(true)', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => ''),
				disabled: boxWith(() => false)
			});

			state.setFocused(true);
			expect(state.focused).toBe(true);
		});

		it('should update focused state with setFocused(false)', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => ''),
				disabled: boxWith(() => false)
			});

			state.setFocused(true);
			state.setFocused(false);
			expect(state.focused).toBe(false);
		});
	});

	describe('Clear Functionality', () => {
		it('should clear value to empty string', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => 'Hello World'),
				disabled: boxWith(() => false)
			});

			state.clear();
			expect(state.value).toBe('');
		});

		it('should call onChange when cleared', () => {
			const onChange = vi.fn();
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => 'Hello'),
				disabled: boxWith(() => false),
				onChange: boxWith(() => onChange)
			});

			state.clear();

			expect(onChange).toHaveBeenCalledWith('');
		});
	});

	describe('Shared Props', () => {
		it('should include data-disabled when disabled', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => ''),
				disabled: boxWith(() => true)
			});

			expect(state.sharedProps['data-disabled']).toBe('');
		});

		it('should NOT include data-disabled when not disabled', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => ''),
				disabled: boxWith(() => false)
			});

			expect(state.sharedProps['data-disabled']).toBeUndefined();
		});

		it('should include data-focused when focused', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => ''),
				disabled: boxWith(() => false)
			});

			state.setFocused(true);
			expect(state.sharedProps['data-focused']).toBe('');
		});

		it('should include data-empty when empty', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => ''),
				disabled: boxWith(() => false)
			});

			expect(state.sharedProps['data-empty']).toBe('');
		});

		it('should NOT include data-empty when has content', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => 'Hello'),
				disabled: boxWith(() => false)
			});

			expect(state.sharedProps['data-empty']).toBeUndefined();
		});
	});

	describe('Edge Cases', () => {
		it('should handle very long text', () => {
			const longText = 'a'.repeat(10000);
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => longText),
				disabled: boxWith(() => false)
			});

			expect(state.characterCount).toBe(10000);
			expect(state.isEmpty).toBe(false);
		});

		it('should handle emoji and unicode characters', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => '👋 Hello 世界'),
				disabled: boxWith(() => false)
			});

			expect(state.value).toBe('👋 Hello 世界');
			expect(state.isEmpty).toBe(false);
		});

		it('should handle newlines and special characters', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => 'Line 1\nLine 2\tTabbed'),
				disabled: boxWith(() => false)
			});

			expect(state.value).toBe('Line 1\nLine 2\tTabbed');
			expect(state.isEmpty).toBe(false);
		});

		it('should handle rapid state changes', () => {
			const state = new ChatInputRootState({
				value: boxWith(() => undefined),
				defaultValue: boxWith(() => ''),
				disabled: boxWith(() => false)
			});

			for (let i = 0; i < 100; i++) {
				state.updateValue(`Value ${i}`);
			}

			expect(state.value).toBe('Value 99');
		});
	});
});
