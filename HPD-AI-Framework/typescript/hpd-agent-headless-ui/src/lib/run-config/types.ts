import type { Snippet } from 'svelte';
import type { RunConfigState } from './run-config.svelte.js';

// ============================================
// Supporting types
// ============================================

export interface ProviderOption {
	key: string;
	label: string;
	models: ModelOption[];
}

export interface ModelOption {
	id: string;
	label: string;
}

export interface PermissionOverrideItem {
	key: string;
	label: string;
	value: boolean | undefined;
}

// ============================================
// ModelSelector
// ============================================

export interface RunConfigModelSelectorHTMLProps {
	'data-run-config-model': '';
	'data-disabled'?: '';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface RunConfigModelSelectorSnippetProps {
	providerKey: string | undefined;
	modelId: string | undefined;
	providers: ProviderOption[];
	disabled: boolean;
	setModel: (providerKey: string | undefined, modelId: string | undefined) => void;
}

export interface RunConfigModelSelectorProps {
	runConfig: RunConfigState;
	providers?: ProviderOption[];
	disabled?: boolean;
	child?: Snippet<[RunConfigModelSelectorSnippetProps & { props: RunConfigModelSelectorHTMLProps }]>;
	children?: Snippet<[RunConfigModelSelectorSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// TemperatureSlider
// ============================================

export interface RunConfigTemperatureSliderHTMLProps {
	'data-run-config-temperature': '';
	'data-disabled'?: '';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface RunConfigTemperatureSliderSnippetProps {
	value: number | undefined;
	min: number;
	max: number;
	step: number;
	disabled: boolean;
	setValue: (value: number | undefined) => void;
}

export interface RunConfigTemperatureSliderProps {
	runConfig: RunConfigState;
	min?: number;
	max?: number;
	step?: number;
	disabled?: boolean;
	child?: Snippet<[RunConfigTemperatureSliderSnippetProps & { props: RunConfigTemperatureSliderHTMLProps }]>;
	children?: Snippet<[RunConfigTemperatureSliderSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// TopPSlider
// ============================================

export interface RunConfigTopPSliderHTMLProps {
	'data-run-config-top-p': '';
	'data-disabled'?: '';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface RunConfigTopPSliderSnippetProps {
	value: number | undefined;
	min: number;
	max: number;
	step: number;
	disabled: boolean;
	setValue: (value: number | undefined) => void;
}

export interface RunConfigTopPSliderProps {
	runConfig: RunConfigState;
	min?: number;
	max?: number;
	step?: number;
	disabled?: boolean;
	child?: Snippet<[RunConfigTopPSliderSnippetProps & { props: RunConfigTopPSliderHTMLProps }]>;
	children?: Snippet<[RunConfigTopPSliderSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// MaxTokensInput
// ============================================

export interface RunConfigMaxTokensInputHTMLProps {
	'data-run-config-max-tokens': '';
	'data-disabled'?: '';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface RunConfigMaxTokensInputSnippetProps {
	value: number | undefined;
	min: number;
	max: number | undefined;
	disabled: boolean;
	setValue: (value: number | undefined) => void;
}

export interface RunConfigMaxTokensInputProps {
	runConfig: RunConfigState;
	min?: number;
	max?: number;
	disabled?: boolean;
	child?: Snippet<[RunConfigMaxTokensInputSnippetProps & { props: RunConfigMaxTokensInputHTMLProps }]>;
	children?: Snippet<[RunConfigMaxTokensInputSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// SystemInstructionsInput
// ============================================

export interface RunConfigSystemInstructionsInputHTMLProps {
	'data-run-config-system-instructions': '';
	'data-disabled'?: '';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface RunConfigSystemInstructionsInputSnippetProps {
	value: string | undefined;
	disabled: boolean;
	setValue: (value: string | undefined) => void;
}

export interface RunConfigSystemInstructionsInputProps {
	runConfig: RunConfigState;
	disabled?: boolean;
	child?: Snippet<[RunConfigSystemInstructionsInputSnippetProps & { props: RunConfigSystemInstructionsInputHTMLProps }]>;
	children?: Snippet<[RunConfigSystemInstructionsInputSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// PermissionOverridesPanel
// ============================================

export interface RunConfigPermissionOverridesPanelHTMLProps {
	'data-run-config-permission-overrides': '';
	'data-disabled'?: '';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface RunConfigPermissionOverridesPanelSnippetProps {
	items: PermissionOverrideItem[];
	disabled: boolean;
	setOverride: (key: string, value: boolean | undefined) => void;
}

export interface RunConfigPermissionOverridesPanelProps {
	runConfig: RunConfigState;
	permissions?: string[];
	disabled?: boolean;
	child?: Snippet<[RunConfigPermissionOverridesPanelSnippetProps & { props: RunConfigPermissionOverridesPanelHTMLProps }]>;
	children?: Snippet<[RunConfigPermissionOverridesPanelSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// SkipToolsToggle
// ============================================

export interface RunConfigSkipToolsToggleHTMLProps {
	'data-run-config-skip-tools': '';
	'data-disabled'?: '';
	'data-checked'?: '';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface RunConfigSkipToolsToggleSnippetProps {
	value: boolean | undefined;
	disabled: boolean;
	setValue: (value: boolean | undefined) => void;
}

export interface RunConfigSkipToolsToggleProps {
	runConfig: RunConfigState;
	disabled?: boolean;
	child?: Snippet<[RunConfigSkipToolsToggleSnippetProps & { props: RunConfigSkipToolsToggleHTMLProps }]>;
	children?: Snippet<[RunConfigSkipToolsToggleSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// RunTimeoutInput
// ============================================

export interface RunConfigRunTimeoutInputHTMLProps {
	'data-run-config-run-timeout': '';
	'data-disabled'?: '';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface RunConfigRunTimeoutInputSnippetProps {
	value: string | undefined;
	disabled: boolean;
	setValue: (value: string | undefined) => void;
}

export interface RunConfigRunTimeoutInputProps {
	runConfig: RunConfigState;
	disabled?: boolean;
	child?: Snippet<[RunConfigRunTimeoutInputSnippetProps & { props: RunConfigRunTimeoutInputHTMLProps }]>;
	children?: Snippet<[RunConfigRunTimeoutInputSnippetProps]>;
	[key: string]: unknown;
}
