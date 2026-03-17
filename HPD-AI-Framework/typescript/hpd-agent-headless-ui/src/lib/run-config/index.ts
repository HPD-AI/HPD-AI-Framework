/**
 * RunConfig headless components
 *
 * Per-send run configuration controls: model picker, temperature, top-p,
 * max tokens, system instructions, permission overrides, skip-tools toggle,
 * and run timeout.
 *
 * @example
 * ```svelte
 * <script>
 *   import { RunConfig } from '@hpd/hpd-agent-headless-ui';
 *
 *   const runConfig = new RunConfig.State();
 * </script>
 *
 * <RunConfig.ModelSelector {runConfig} {providers}>
 *   {#snippet children(s)}
 *     <select onchange={...}> ... </select>
 *   {/snippet}
 * </RunConfig.ModelSelector>
 *
 * <RunConfig.TemperatureSlider {runConfig}>
 *   {#snippet children(s)}
 *     <input type="range" min={s.min} max={s.max} step={s.step}
 *       value={s.value ?? 0.7} oninput={(e) => s.setValue(+e.currentTarget.value)} />
 *   {/snippet}
 * </RunConfig.TemperatureSlider>
 * ```
 */

export * from './exports.ts';
export { RunConfigState as State, RunConfigState } from './run-config.svelte.ts';
export {
	RunConfigModelSelectorState,
	RunConfigTemperatureSliderState,
	RunConfigTopPSliderState,
	RunConfigMaxTokensInputState,
	RunConfigSystemInstructionsInputState,
	RunConfigPermissionOverridesPanelState,
	RunConfigSkipToolsToggleState,
	RunConfigRunTimeoutInputState,
} from './run-config.svelte.ts';

export type {
	RunConfigModelSelectorProps,
	RunConfigModelSelectorHTMLProps,
	RunConfigModelSelectorSnippetProps,
	RunConfigTemperatureSliderProps,
	RunConfigTemperatureSliderHTMLProps,
	RunConfigTemperatureSliderSnippetProps,
	RunConfigTopPSliderProps,
	RunConfigTopPSliderHTMLProps,
	RunConfigTopPSliderSnippetProps,
	RunConfigMaxTokensInputProps,
	RunConfigMaxTokensInputHTMLProps,
	RunConfigMaxTokensInputSnippetProps,
	RunConfigSystemInstructionsInputProps,
	RunConfigSystemInstructionsInputHTMLProps,
	RunConfigSystemInstructionsInputSnippetProps,
	RunConfigPermissionOverridesPanelProps,
	RunConfigPermissionOverridesPanelHTMLProps,
	RunConfigPermissionOverridesPanelSnippetProps,
	RunConfigSkipToolsToggleProps,
	RunConfigSkipToolsToggleHTMLProps,
	RunConfigSkipToolsToggleSnippetProps,
	RunConfigRunTimeoutInputProps,
	RunConfigRunTimeoutInputHTMLProps,
	RunConfigRunTimeoutInputSnippetProps,
	ProviderOption,
	ModelOption,
	PermissionOverrideItem,
} from './types.ts';
