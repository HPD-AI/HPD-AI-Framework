/**
 * RunConfigState unit tests
 *
 * Tests the core state class and all child state classes for the RunConfig
 * headless components. No Svelte runtime required — state classes are plain
 * TypeScript classes whose $state fields are tested via their public getters.
 *
 * Test type: unit (server project — Node environment).
 */

import { describe, it, expect, vi } from 'vitest';
import { boxWith } from 'svelte-toolbelt';
import {
	RunConfigState,
	RunConfigModelSelectorState,
	RunConfigTemperatureSliderState,
	RunConfigTopPSliderState,
	RunConfigMaxTokensInputState,
	RunConfigSystemInstructionsInputState,
	RunConfigPermissionOverridesPanelState,
	RunConfigSkipToolsToggleState,
	RunConfigRunTimeoutInputState,
} from '../run-config.svelte.ts';

// ---------------------------------------------------------------------------
// RunConfigState
// ---------------------------------------------------------------------------

describe('RunConfigState', () => {
	describe('initial state', () => {
		it('starts with value === undefined when nothing is set', () => {
			const state = new RunConfigState();
			expect(state.value).toBeUndefined();
		});

		it('all individual getters return undefined initially', () => {
			const state = new RunConfigState();
			expect(state.providerKey).toBeUndefined();
			expect(state.modelId).toBeUndefined();
			expect(state.temperature).toBeUndefined();
			expect(state.maxOutputTokens).toBeUndefined();
			expect(state.topP).toBeUndefined();
			expect(state.additionalSystemInstructions).toBeUndefined();
			expect(state.skipTools).toBeUndefined();
			expect(state.runTimeout).toBeUndefined();
		});

		it('permissionOverrides is an empty object initially', () => {
			const state = new RunConfigState();
			expect(state.permissionOverrides).toEqual({});
		});

		it('chat is undefined when no chat fields are set', () => {
			const state = new RunConfigState();
			expect(state.chat).toBeUndefined();
		});
	});

	describe('setModel()', () => {
		it('sets providerKey and modelId', () => {
			const state = new RunConfigState();
			state.setModel('anthropic', 'claude-sonnet-4-6');
			expect(state.providerKey).toBe('anthropic');
			expect(state.modelId).toBe('claude-sonnet-4-6');
		});

		it('value includes providerKey and modelId after setModel', () => {
			const state = new RunConfigState();
			state.setModel('anthropic', 'claude-sonnet-4-6');
			expect(state.value?.providerKey).toBe('anthropic');
			expect(state.value?.modelId).toBe('claude-sonnet-4-6');
		});

		it('can clear model by passing undefined', () => {
			const state = new RunConfigState();
			state.setModel('anthropic', 'claude-sonnet-4-6');
			state.setModel(undefined, undefined);
			expect(state.providerKey).toBeUndefined();
			expect(state.modelId).toBeUndefined();
		});
	});

	describe('setTemperature()', () => {
		it('sets temperature and creates chat sub-object', () => {
			const state = new RunConfigState();
			state.setTemperature(0.7);
			expect(state.temperature).toBe(0.7);
			expect(state.chat).toEqual({ temperature: 0.7, maxOutputTokens: undefined, topP: undefined });
		});

		it('collapses chat to undefined when temperature cleared and other chat fields unset', () => {
			const state = new RunConfigState();
			state.setTemperature(0.7);
			state.setTemperature(undefined);
			expect(state.chat).toBeUndefined();
		});

		it('value.chat.temperature reflects the set value', () => {
			const state = new RunConfigState();
			state.setTemperature(0.5);
			expect(state.value?.chat?.temperature).toBe(0.5);
		});
	});

	describe('setMaxTokens()', () => {
		it('sets maxOutputTokens', () => {
			const state = new RunConfigState();
			state.setMaxTokens(4096);
			expect(state.maxOutputTokens).toBe(4096);
			expect(state.chat?.maxOutputTokens).toBe(4096);
		});

		it('chat survives when topP is also set and temperature is cleared', () => {
			const state = new RunConfigState();
			state.setTemperature(0.7);
			state.setMaxTokens(2048);
			state.setTemperature(undefined);
			// maxOutputTokens still set — chat must not collapse
			expect(state.chat).toBeDefined();
			expect(state.chat?.maxOutputTokens).toBe(2048);
		});
	});

	describe('setTopP()', () => {
		it('sets topP', () => {
			const state = new RunConfigState();
			state.setTopP(0.9);
			expect(state.topP).toBe(0.9);
			expect(state.chat?.topP).toBe(0.9);
		});
	});

	describe('setAdditionalSystemInstructions()', () => {
		it('sets additionalSystemInstructions', () => {
			const state = new RunConfigState();
			state.setAdditionalSystemInstructions('Be concise.');
			expect(state.additionalSystemInstructions).toBe('Be concise.');
			expect(state.value?.additionalSystemInstructions).toBe('Be concise.');
		});

		it('clears when set to undefined', () => {
			const state = new RunConfigState();
			state.setAdditionalSystemInstructions('Be concise.');
			state.setAdditionalSystemInstructions(undefined);
			expect(state.additionalSystemInstructions).toBeUndefined();
		});
	});

	describe('setPermissionOverride()', () => {
		it('adds a key/value pair', () => {
			const state = new RunConfigState();
			state.setPermissionOverride('read_file', true);
			expect(state.permissionOverrides).toEqual({ read_file: true });
		});

		it('removes a key when value is undefined', () => {
			const state = new RunConfigState();
			state.setPermissionOverride('read_file', true);
			state.setPermissionOverride('read_file', undefined);
			expect(state.permissionOverrides).toEqual({});
		});

		it('can hold multiple overrides simultaneously', () => {
			const state = new RunConfigState();
			state.setPermissionOverride('read_file', true);
			state.setPermissionOverride('write_file', false);
			expect(state.permissionOverrides).toEqual({ read_file: true, write_file: false });
		});

		it('permissionOverrides collapses to undefined in value when empty', () => {
			const state = new RunConfigState();
			state.setPermissionOverride('read_file', true);
			state.setPermissionOverride('read_file', undefined);
			// No other fields set — value should be undefined entirely
			expect(state.value).toBeUndefined();
		});

		it('permissionOverrides is present in value when at least one entry exists', () => {
			const state = new RunConfigState();
			state.setPermissionOverride('read_file', true);
			expect(state.value?.permissionOverrides).toEqual({ read_file: true });
		});
	});

	describe('setSkipTools()', () => {
		it('sets skipTools to true', () => {
			const state = new RunConfigState();
			state.setSkipTools(true);
			expect(state.skipTools).toBe(true);
			expect(state.value?.skipTools).toBe(true);
		});

		it('sets skipTools to false', () => {
			const state = new RunConfigState();
			state.setSkipTools(false);
			expect(state.value?.skipTools).toBe(false);
		});
	});

	describe('setRunTimeout()', () => {
		it('sets runTimeout', () => {
			const state = new RunConfigState();
			state.setRunTimeout('PT5M');
			expect(state.runTimeout).toBe('PT5M');
			expect(state.value?.runTimeout).toBe('PT5M');
		});
	});

	describe('value — clean serialization', () => {
		it('value only contains set fields — no undefined keys', () => {
			const state = new RunConfigState();
			state.setModel('openai', 'gpt-4o');
			const v = state.value!;
			const keys = Object.keys(v);
			expect(keys).toContain('providerKey');
			expect(keys).toContain('modelId');
			// Fields that were never set must be absent
			for (const key of keys) {
				expect((v as Record<string, unknown>)[key]).not.toBeUndefined();
			}
		});

		it('JSON.stringify omits undefined fields', () => {
			const state = new RunConfigState();
			state.setTemperature(0.8);
			const json = JSON.stringify(state.value);
			const parsed = JSON.parse(json);
			expect(parsed).toEqual({ chat: { temperature: 0.8 } });
		});
	});

	describe('reset()', () => {
		it('returns all fields to undefined after being set', () => {
			const state = new RunConfigState();
			state.setModel('anthropic', 'claude-sonnet-4-6');
			state.setTemperature(0.7);
			state.setMaxTokens(4096);
			state.setTopP(0.9);
			state.setAdditionalSystemInstructions('Be concise.');
			state.setPermissionOverride('read_file', true);
			state.setSkipTools(true);
			state.setRunTimeout('PT5M');

			state.reset();

			expect(state.value).toBeUndefined();
			expect(state.permissionOverrides).toEqual({});
		});

		it('can be called on a fresh state without error', () => {
			const state = new RunConfigState();
			expect(() => state.reset()).not.toThrow();
			expect(state.value).toBeUndefined();
		});
	});
});

// ---------------------------------------------------------------------------
// RunConfigModelSelectorState
// ---------------------------------------------------------------------------

describe('RunConfigModelSelectorState', () => {
	function makeState(opts: {
		providerKey?: string;
		modelId?: string;
		providers?: { key: string; label: string; models: { id: string; label: string }[] }[];
		disabled?: boolean;
	} = {}) {
		const root = new RunConfigState();
		if (opts.providerKey !== undefined || opts.modelId !== undefined) {
			root.setModel(opts.providerKey, opts.modelId);
		}
		return new RunConfigModelSelectorState({
			runConfig: boxWith(() => root),
			providers: boxWith(() => opts.providers ?? []),
			disabled: boxWith(() => opts.disabled ?? false),
		});
	}

	it('providerKey reads from root state', () => {
		const state = makeState({ providerKey: 'anthropic', modelId: 'claude-sonnet-4-6' });
		expect(state.providerKey).toBe('anthropic');
	});

	it('modelId reads from root state', () => {
		const state = makeState({ providerKey: 'anthropic', modelId: 'claude-sonnet-4-6' });
		expect(state.modelId).toBe('claude-sonnet-4-6');
	});

	it('providers reads from the box', () => {
		const providers = [{ key: 'openai', label: 'OpenAI', models: [{ id: 'gpt-4o', label: 'GPT-4o' }] }];
		const state = makeState({ providers });
		expect(state.providers).toBe(providers);
	});

	it('setModel() updates the root state', () => {
		const root = new RunConfigState();
		const child = new RunConfigModelSelectorState({
			runConfig: boxWith(() => root),
			providers: boxWith(() => []),
			disabled: boxWith(() => false),
		});
		child.setModel('anthropic', 'claude-opus-4-6');
		expect(root.providerKey).toBe('anthropic');
		expect(root.modelId).toBe('claude-opus-4-6');
	});

	it('props has data-run-config-model always', () => {
		const state = makeState();
		expect(state.props['data-run-config-model']).toBe('');
	});

	it('props has data-disabled when disabled: true', () => {
		const state = makeState({ disabled: true });
		expect(state.props['data-disabled']).toBe('');
	});

	it('props omits data-disabled when disabled: false', () => {
		const state = makeState({ disabled: false });
		expect(state.props['data-disabled']).toBeUndefined();
	});

	it('snippetProps contains all required fields', () => {
		const state = makeState({ providerKey: 'anthropic', modelId: 'claude-sonnet-4-6' });
		const sp = state.snippetProps;
		expect(sp).toHaveProperty('providerKey');
		expect(sp).toHaveProperty('modelId');
		expect(sp).toHaveProperty('providers');
		expect(sp).toHaveProperty('disabled');
		expect(sp).toHaveProperty('setModel');
		expect(typeof sp.setModel).toBe('function');
	});
});

// ---------------------------------------------------------------------------
// RunConfigTemperatureSliderState
// ---------------------------------------------------------------------------

describe('RunConfigTemperatureSliderState', () => {
	function makeState(opts: { temperature?: number; min?: number; max?: number; step?: number; disabled?: boolean } = {}) {
		const root = new RunConfigState();
		if (opts.temperature !== undefined) root.setTemperature(opts.temperature);
		return new RunConfigTemperatureSliderState({
			runConfig: boxWith(() => root),
			min: boxWith(() => opts.min ?? 0),
			max: boxWith(() => opts.max ?? 1),
			step: boxWith(() => opts.step ?? 0.01),
			disabled: boxWith(() => opts.disabled ?? false),
		});
	}

	it('value reads temperature from root', () => {
		const state = makeState({ temperature: 0.7 });
		expect(state.value).toBe(0.7);
	});

	it('value is undefined when temperature not set', () => {
		const state = makeState();
		expect(state.value).toBeUndefined();
	});

	it('setValue() updates root temperature', () => {
		const root = new RunConfigState();
		const child = new RunConfigTemperatureSliderState({
			runConfig: boxWith(() => root),
			min: boxWith(() => 0),
			max: boxWith(() => 1),
			step: boxWith(() => 0.01),
			disabled: boxWith(() => false),
		});
		child.setValue(0.5);
		expect(root.temperature).toBe(0.5);
	});

	it('min, max, step default values read from box', () => {
		const state = makeState({ min: 0, max: 2, step: 0.1 });
		expect(state.min).toBe(0);
		expect(state.max).toBe(2);
		expect(state.step).toBe(0.1);
	});

	it('props has data-run-config-temperature', () => {
		const state = makeState();
		expect(state.props['data-run-config-temperature']).toBe('');
	});

	it('props has data-disabled when disabled', () => {
		const state = makeState({ disabled: true });
		expect(state.props['data-disabled']).toBe('');
	});

	it('snippetProps contains value, min, max, step, disabled, setValue', () => {
		const state = makeState({ temperature: 0.3 });
		const sp = state.snippetProps;
		expect(sp.value).toBe(0.3);
		expect(sp).toHaveProperty('min');
		expect(sp).toHaveProperty('max');
		expect(sp).toHaveProperty('step');
		expect(sp).toHaveProperty('disabled');
		expect(typeof sp.setValue).toBe('function');
	});
});

// ---------------------------------------------------------------------------
// RunConfigTopPSliderState
// ---------------------------------------------------------------------------

describe('RunConfigTopPSliderState', () => {
	function makeState(opts: { topP?: number; disabled?: boolean } = {}) {
		const root = new RunConfigState();
		if (opts.topP !== undefined) root.setTopP(opts.topP);
		return new RunConfigTopPSliderState({
			runConfig: boxWith(() => root),
			min: boxWith(() => 0),
			max: boxWith(() => 1),
			step: boxWith(() => 0.01),
			disabled: boxWith(() => opts.disabled ?? false),
		});
	}

	it('value reads topP from root', () => {
		const state = makeState({ topP: 0.9 });
		expect(state.value).toBe(0.9);
	});

	it('setValue() updates root topP', () => {
		const root = new RunConfigState();
		const child = new RunConfigTopPSliderState({
			runConfig: boxWith(() => root),
			min: boxWith(() => 0),
			max: boxWith(() => 1),
			step: boxWith(() => 0.01),
			disabled: boxWith(() => false),
		});
		child.setValue(0.85);
		expect(root.topP).toBe(0.85);
	});

	it('props has data-run-config-top-p', () => {
		const state = makeState();
		expect(state.props['data-run-config-top-p']).toBe('');
	});
});

// ---------------------------------------------------------------------------
// RunConfigMaxTokensInputState
// ---------------------------------------------------------------------------

describe('RunConfigMaxTokensInputState', () => {
	function makeState(opts: { maxTokens?: number; min?: number; max?: number; disabled?: boolean } = {}) {
		const root = new RunConfigState();
		if (opts.maxTokens !== undefined) root.setMaxTokens(opts.maxTokens);
		return new RunConfigMaxTokensInputState({
			runConfig: boxWith(() => root),
			min: boxWith(() => opts.min ?? 1),
			max: boxWith(() => opts.max),
			disabled: boxWith(() => opts.disabled ?? false),
		});
	}

	it('value reads maxOutputTokens from root', () => {
		const state = makeState({ maxTokens: 4096 });
		expect(state.value).toBe(4096);
	});

	it('setValue() updates root maxOutputTokens', () => {
		const root = new RunConfigState();
		const child = new RunConfigMaxTokensInputState({
			runConfig: boxWith(() => root),
			min: boxWith(() => 1),
			max: boxWith(() => undefined),
			disabled: boxWith(() => false),
		});
		child.setValue(8192);
		expect(root.maxOutputTokens).toBe(8192);
	});

	it('props has data-run-config-max-tokens', () => {
		const state = makeState();
		expect(state.props['data-run-config-max-tokens']).toBe('');
	});

	it('max can be undefined (unbounded)', () => {
		const state = makeState({ max: undefined });
		expect(state.max).toBeUndefined();
	});
});

// ---------------------------------------------------------------------------
// RunConfigSystemInstructionsInputState
// ---------------------------------------------------------------------------

describe('RunConfigSystemInstructionsInputState', () => {
	function makeState(opts: { value?: string; disabled?: boolean } = {}) {
		const root = new RunConfigState();
		if (opts.value !== undefined) root.setAdditionalSystemInstructions(opts.value);
		return new RunConfigSystemInstructionsInputState({
			runConfig: boxWith(() => root),
			disabled: boxWith(() => opts.disabled ?? false),
		});
	}

	it('value reads additionalSystemInstructions from root', () => {
		const state = makeState({ value: 'Be terse.' });
		expect(state.value).toBe('Be terse.');
	});

	it('setValue() updates root additionalSystemInstructions', () => {
		const root = new RunConfigState();
		const child = new RunConfigSystemInstructionsInputState({
			runConfig: boxWith(() => root),
			disabled: boxWith(() => false),
		});
		child.setValue('Focus on safety.');
		expect(root.additionalSystemInstructions).toBe('Focus on safety.');
	});

	it('props has data-run-config-system-instructions', () => {
		const state = makeState();
		expect(state.props['data-run-config-system-instructions']).toBe('');
	});
});

// ---------------------------------------------------------------------------
// RunConfigPermissionOverridesPanelState
// ---------------------------------------------------------------------------

describe('RunConfigPermissionOverridesPanelState', () => {
	function makeState(opts: {
		permissions?: string[];
		overrides?: Record<string, boolean>;
		disabled?: boolean;
	} = {}) {
		const root = new RunConfigState();
		for (const [k, v] of Object.entries(opts.overrides ?? {})) {
			root.setPermissionOverride(k, v);
		}
		return new RunConfigPermissionOverridesPanelState({
			runConfig: boxWith(() => root),
			permissions: boxWith(() => opts.permissions ?? []),
			disabled: boxWith(() => opts.disabled ?? false),
		});
	}

	it('items maps permission keys to PermissionOverrideItem shape', () => {
		const state = makeState({ permissions: ['read_file', 'write_file'] });
		expect(state.items).toHaveLength(2);
		expect(state.items[0]).toMatchObject({ key: 'read_file', label: 'read_file' });
		expect(state.items[1]).toMatchObject({ key: 'write_file', label: 'write_file' });
	});

	it('items includes the current override value', () => {
		const state = makeState({
			permissions: ['read_file'],
			overrides: { read_file: true },
		});
		expect(state.items[0].value).toBe(true);
	});

	it('items shows undefined value for permissions without an override', () => {
		const state = makeState({ permissions: ['exec'] });
		expect(state.items[0].value).toBeUndefined();
	});

	it('setOverride() updates root permissionOverrides', () => {
		const root = new RunConfigState();
		const child = new RunConfigPermissionOverridesPanelState({
			runConfig: boxWith(() => root),
			permissions: boxWith(() => ['read_file']),
			disabled: boxWith(() => false),
		});
		child.setOverride('read_file', false);
		expect(root.permissionOverrides['read_file']).toBe(false);
	});

	it('props has data-run-config-permission-overrides', () => {
		const state = makeState();
		expect(state.props['data-run-config-permission-overrides']).toBe('');
	});
});

// ---------------------------------------------------------------------------
// RunConfigSkipToolsToggleState
// ---------------------------------------------------------------------------

describe('RunConfigSkipToolsToggleState', () => {
	function makeState(opts: { skipTools?: boolean; disabled?: boolean } = {}) {
		const root = new RunConfigState();
		if (opts.skipTools !== undefined) root.setSkipTools(opts.skipTools);
		return new RunConfigSkipToolsToggleState({
			runConfig: boxWith(() => root),
			disabled: boxWith(() => opts.disabled ?? false),
		});
	}

	it('value reads skipTools from root', () => {
		const state = makeState({ skipTools: true });
		expect(state.value).toBe(true);
	});

	it('setValue() updates root skipTools', () => {
		const root = new RunConfigState();
		const child = new RunConfigSkipToolsToggleState({
			runConfig: boxWith(() => root),
			disabled: boxWith(() => false),
		});
		child.setValue(true);
		expect(root.skipTools).toBe(true);
	});

	it('props has data-run-config-skip-tools', () => {
		const state = makeState();
		expect(state.props['data-run-config-skip-tools']).toBe('');
	});

	it('props has data-checked when value is true', () => {
		const state = makeState({ skipTools: true });
		expect(state.props['data-checked']).toBe('');
	});

	it('props omits data-checked when value is undefined', () => {
		const state = makeState();
		expect(state.props['data-checked']).toBeUndefined();
	});

	it('props omits data-checked when value is false', () => {
		const state = makeState({ skipTools: false });
		expect(state.props['data-checked']).toBeUndefined();
	});
});

// ---------------------------------------------------------------------------
// RunConfigRunTimeoutInputState
// ---------------------------------------------------------------------------

describe('RunConfigRunTimeoutInputState', () => {
	function makeState(opts: { timeout?: string; disabled?: boolean } = {}) {
		const root = new RunConfigState();
		if (opts.timeout !== undefined) root.setRunTimeout(opts.timeout);
		return new RunConfigRunTimeoutInputState({
			runConfig: boxWith(() => root),
			disabled: boxWith(() => opts.disabled ?? false),
		});
	}

	it('value reads runTimeout from root', () => {
		const state = makeState({ timeout: 'PT5M' });
		expect(state.value).toBe('PT5M');
	});

	it('setValue() updates root runTimeout', () => {
		const root = new RunConfigState();
		const child = new RunConfigRunTimeoutInputState({
			runConfig: boxWith(() => root),
			disabled: boxWith(() => false),
		});
		child.setValue('PT30S');
		expect(root.runTimeout).toBe('PT30S');
	});

	it('props has data-run-config-run-timeout', () => {
		const state = makeState();
		expect(state.props['data-run-config-run-timeout']).toBe('');
	});
});
