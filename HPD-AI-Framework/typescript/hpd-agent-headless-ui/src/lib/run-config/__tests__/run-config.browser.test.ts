/**
 * RunConfig Browser Component Tests
 *
 * Verifies that each RunConfig component renders the correct data attributes,
 * exposes the right snippet props through the DOM, and mutates RunConfigState
 * when setters are called via button clicks in the test harness.
 *
 * Test type: browser (chromium) — vitest-browser-svelte.
 */

import { describe, it, expect } from 'vitest';
import { render } from 'vitest-browser-svelte';
import { page } from 'vitest/browser';
import RunConfigTest from './run-config-test.svelte';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function setup(props: {
	disabled?: boolean;
	providers?: { key: string; label: string; models: { id: string; label: string }[] }[];
	permissions?: string[];
	initialTemperature?: number;
	initialSkipTools?: boolean;
} = {}) {
	render(RunConfigTest, { props } as any);
}

// ---------------------------------------------------------------------------
// ModelSelector — data attributes
// ---------------------------------------------------------------------------

describe('RunConfig.ModelSelector — data attributes', () => {
	it('renders wrapper with data-run-config-model', async () => {
		setup();
		const wrapper = page.getByTestId('model-selector-wrapper');
		await expect.element(wrapper).toBeInTheDocument();
	});

	it('does not have data-disabled when disabled is false', async () => {
		setup({ disabled: false });
		await expect.element(page.getByTestId('model-selector-wrapper')).toBeInTheDocument();
	});

	it('has data-disabled when disabled is true', async () => {
		setup({ disabled: true });
		await expect.element(page.getByTestId('model-selector-wrapper')).toBeInTheDocument();
	});
});

// ---------------------------------------------------------------------------
// ModelSelector — snippet props & state mutation
// ---------------------------------------------------------------------------

describe('RunConfig.ModelSelector — snippet props', () => {
	it('setModel() updates providerKey and modelId in DOM', async () => {
		setup();
		const inner = page.getByTestId('model-selector-inner');

		// Before click: empty
		await expect.element(inner).toHaveAttribute('data-provider-key', '');
		await expect.element(inner).toHaveAttribute('data-model-id', '');

		await page.getByTestId('set-model-btn').click();

		await expect.element(inner).toHaveAttribute('data-provider-key', 'anthropic');
		await expect.element(inner).toHaveAttribute('data-model-id', 'claude-sonnet-4-6');
	});

	it('clearing model returns providerKey and modelId to empty', async () => {
		setup();
		await page.getByTestId('set-model-btn').click();
		await page.getByTestId('clear-model-btn').click();

		const inner = page.getByTestId('model-selector-inner');
		await expect.element(inner).toHaveAttribute('data-provider-key', '');
		await expect.element(inner).toHaveAttribute('data-model-id', '');
	});

	it('runConfig.value updates when model is set', async () => {
		setup();
		await page.getByTestId('set-model-btn').click();

		const valueEl = page.getByTestId('run-config-value');
		await expect.element(valueEl).toHaveTextContent('anthropic');
		await expect.element(valueEl).toHaveTextContent('claude-sonnet-4-6');
	});
});

// ---------------------------------------------------------------------------
// TemperatureSlider — data attributes
// ---------------------------------------------------------------------------

describe('RunConfig.TemperatureSlider — data attributes', () => {
	it('renders wrapper with data-run-config-temperature', async () => {
		setup();
		await expect.element(page.getByTestId('temperature-wrapper')).toBeInTheDocument();
	});

	it('does not have data-disabled when disabled is false', async () => {
		setup({ disabled: false });
		await expect.element(page.getByTestId('temperature-wrapper')).toBeInTheDocument();
	});

	it('has data-disabled when disabled is true', async () => {
		setup({ disabled: true });
		await expect.element(page.getByTestId('temperature-wrapper')).toBeInTheDocument();
	});
});

// ---------------------------------------------------------------------------
// TemperatureSlider — snippet props & state mutation
// ---------------------------------------------------------------------------

describe('RunConfig.TemperatureSlider — snippet props', () => {
	it('exposes correct default min/max/step', async () => {
		setup();
		await expect.element(page.getByTestId('temp-min')).toHaveTextContent('0');
		await expect.element(page.getByTestId('temp-max')).toHaveTextContent('1');
		await expect.element(page.getByTestId('temp-step')).toHaveTextContent('0.01');
	});

	it('exposes disabled state via snippet', async () => {
		setup({ disabled: true });
		await expect.element(page.getByTestId('temp-disabled')).toHaveTextContent('true');
	});

	it('setValue(0.7) updates data-value and run-config-value', async () => {
		setup();
		await page.getByTestId('set-temp-btn').click();

		const inner = page.getByTestId('temperature-inner');
		await expect.element(inner).toHaveAttribute('data-value', '0.7');

		const valueEl = page.getByTestId('run-config-value');
		await expect.element(valueEl).toHaveTextContent('0.7');
	});

	it('setValue(undefined) clears temperature and collapses value', async () => {
		setup();
		await page.getByTestId('set-temp-btn').click();
		await page.getByTestId('clear-temp-btn').click();

		const inner = page.getByTestId('temperature-inner');
		await expect.element(inner).toHaveAttribute('data-value', '');

		// With only temperature set, clearing should collapse value to null
		const valueEl = page.getByTestId('run-config-value');
		await expect.element(valueEl).toHaveTextContent('null');
	});
});

// ---------------------------------------------------------------------------
// TemperatureSlider — child snippet pattern
// ---------------------------------------------------------------------------

describe('RunConfig.TemperatureSlider — child snippet pattern', () => {
	it('child snippet receives props spread onto the element', async () => {
		setup();
		const el = page.getByTestId('temperature-child-pattern');
		// The child pattern spreads state.props, so data-run-config-temperature must be present
		await expect.element(el).toHaveAttribute('data-run-config-temperature');
	});

	it('child snippet shows value correctly', async () => {
		setup();
		await expect.element(page.getByTestId('child-pattern-value')).toHaveTextContent('none');

		await page.getByTestId('set-temp-btn').click();
		await expect.element(page.getByTestId('child-pattern-value')).toHaveTextContent('0.7');
	});
});

// ---------------------------------------------------------------------------
// SkipToolsToggle — data attributes
// ---------------------------------------------------------------------------

describe('RunConfig.SkipToolsToggle — data attributes', () => {
	it('renders with data-run-config-skip-tools', async () => {
		setup();
		await expect.element(page.getByTestId('skip-tools-wrapper')).toBeInTheDocument();
	});

	it('does not have data-checked when value is undefined', async () => {
		setup();
		await expect.element(page.getByTestId('skip-tools-wrapper')).toBeInTheDocument();
	});

	it('has data-checked when value is true', async () => {
		setup({ initialSkipTools: true });
		await expect.element(page.getByTestId('skip-tools-wrapper')).toBeInTheDocument();
	});

	it('does not have data-checked when value is false', async () => {
		setup({ initialSkipTools: false });
		await expect.element(page.getByTestId('skip-tools-wrapper')).toBeInTheDocument();
	});
});

// ---------------------------------------------------------------------------
// SkipToolsToggle — snippet props & state mutation
// ---------------------------------------------------------------------------

describe('RunConfig.SkipToolsToggle — snippet props', () => {
	it('clicking Enable sets skipTools to true', async () => {
		setup();
		await page.getByTestId('skip-tools-on-btn').click();

		const valueEl = page.getByTestId('run-config-value');
		await expect.element(valueEl).toHaveTextContent('"skipTools":true');
	});

	it('clicking Disable sets skipTools to false (no data-checked)', async () => {
		setup();
		await page.getByTestId('skip-tools-on-btn').click();
		await page.getByTestId('skip-tools-off-btn').click();

		const valueEl = page.getByTestId('run-config-value');
		await expect.element(valueEl).toHaveTextContent('"skipTools":false');
	});

	it('clicking Clear returns value to undefined', async () => {
		setup();
		await page.getByTestId('skip-tools-on-btn').click();
		await page.getByTestId('skip-tools-clear-btn').click();

		const valueEl = page.getByTestId('run-config-value');
		await expect.element(valueEl).toHaveTextContent('null');
	});
});

// ---------------------------------------------------------------------------
// PermissionOverridesPanel — snippet props
// ---------------------------------------------------------------------------

describe('RunConfig.PermissionOverridesPanel — snippet props', () => {
	it('renders with data-run-config-permission-overrides', async () => {
		setup({ permissions: ['read_file', 'write_file'] });
		await expect.element(page.getByTestId('permission-overrides-wrapper')).toBeInTheDocument();
	});

	it('items count matches permissions array length', async () => {
		setup({ permissions: ['read_file', 'write_file'] });
		await expect.element(page.getByTestId('perm-count')).toHaveTextContent('2');
	});

	it('items count is zero when permissions is empty', async () => {
		setup({ permissions: [] });
		await expect.element(page.getByTestId('perm-count')).toHaveTextContent('0');
	});

	it('setOverride(key, true) updates run-config-value', async () => {
		setup({ permissions: ['read_file', 'write_file'] });
		await page.getByTestId('perm-allow-read_file').click();

		const valueEl = page.getByTestId('run-config-value');
		await expect.element(valueEl).toHaveTextContent('"read_file":true');
	});

	it('setOverride(key, false) adds deny entry to value', async () => {
		setup({ permissions: ['read_file', 'write_file'] });
		await page.getByTestId('perm-deny-write_file').click();

		const valueEl = page.getByTestId('run-config-value');
		await expect.element(valueEl).toHaveTextContent('"write_file":false');
	});

	it('multiple overrides coexist in value', async () => {
		setup({ permissions: ['read_file', 'write_file'] });
		await page.getByTestId('perm-allow-read_file').click();
		await page.getByTestId('perm-deny-write_file').click();

		const valueEl = page.getByTestId('run-config-value');
		await expect.element(valueEl).toHaveTextContent('"read_file":true');
		await expect.element(valueEl).toHaveTextContent('"write_file":false');
	});
});
