import { Context } from 'runed';
import { type ReadableBox } from 'svelte-toolbelt';
import { boolToEmptyStrOrUndef } from '$lib/internal/attrs.js';
import type { RunConfig, ChatRunConfig } from '@hpd/hpd-agent-client';
export type { RunConfig, ChatRunConfig };
import type {
	RunConfigModelSelectorHTMLProps,
	RunConfigModelSelectorSnippetProps,
	RunConfigTemperatureSliderHTMLProps,
	RunConfigTemperatureSliderSnippetProps,
	RunConfigTopPSliderHTMLProps,
	RunConfigTopPSliderSnippetProps,
	RunConfigMaxTokensInputHTMLProps,
	RunConfigMaxTokensInputSnippetProps,
	RunConfigSystemInstructionsInputHTMLProps,
	RunConfigSystemInstructionsInputSnippetProps,
	RunConfigPermissionOverridesPanelHTMLProps,
	RunConfigPermissionOverridesPanelSnippetProps,
	RunConfigSkipToolsToggleHTMLProps,
	RunConfigSkipToolsToggleSnippetProps,
	RunConfigRunTimeoutInputHTMLProps,
	RunConfigRunTimeoutInputSnippetProps,
	ProviderOption,
	PermissionOverrideItem,
} from './types.js';

// ============================================
// RunConfigState (root — caller-owned)
// ============================================

export class RunConfigState {
	// Mutable slices — $state fields
	#providerKey = $state<string | undefined>(undefined);
	#modelId = $state<string | undefined>(undefined);
	#additionalSystemInstructions = $state<string | undefined>(undefined);
	#temperature = $state<number | undefined>(undefined);
	#maxOutputTokens = $state<number | undefined>(undefined);
	#topP = $state<number | undefined>(undefined);
	#permissionOverrides = $state<Record<string, boolean>>({});
	#skipTools = $state<boolean | undefined>(undefined);
	#runTimeout = $state<string | undefined>(undefined);

	// Plain getters — reactive because they read $state fields
	get providerKey() { return this.#providerKey; }
	get modelId() { return this.#modelId; }
	get temperature() { return this.#temperature; }
	get maxOutputTokens() { return this.#maxOutputTokens; }
	get topP() { return this.#topP; }
	get additionalSystemInstructions() { return this.#additionalSystemInstructions; }
	get skipTools() { return this.#skipTools; }
	get runTimeout() { return this.#runTimeout; }
	get permissionOverrides(): Readonly<Record<string, boolean>> {
		return this.#permissionOverrides;
	}

	// Collapses chat sub-object — undefined when all chat fields are unset
	get chat(): ChatRunConfig | undefined {
		const { temperature, maxOutputTokens, topP } = this;
		if (temperature === undefined && maxOutputTokens === undefined && topP === undefined)
			return undefined;
		return { temperature, maxOutputTokens, topP };
	}

	// Final value handed to send() — undefined when nothing is set
	get value(): RunConfig | undefined {
		const {
			providerKey,
			modelId,
			additionalSystemInstructions,
			chat,
			skipTools,
			runTimeout,
		} = this;
		const permissionOverrides =
			Object.keys(this.#permissionOverrides).length > 0
				? this.#permissionOverrides
				: undefined;
		if (
			providerKey === undefined &&
			modelId === undefined &&
			additionalSystemInstructions === undefined &&
			chat === undefined &&
			permissionOverrides === undefined &&
			skipTools === undefined &&
			runTimeout === undefined
		)
			return undefined;
		return {
			...(providerKey !== undefined && { providerKey }),
			...(modelId !== undefined && { modelId }),
			...(additionalSystemInstructions !== undefined && { additionalSystemInstructions }),
			...(chat !== undefined && { chat }),
			...(permissionOverrides !== undefined && { permissionOverrides }),
			...(skipTools !== undefined && { skipTools }),
			...(runTimeout !== undefined && { runTimeout }),
		};
	}

	// Setters
	setModel(providerKey: string | undefined, modelId: string | undefined) {
		this.#providerKey = providerKey;
		this.#modelId = modelId;
	}
	setTemperature(value: number | undefined) { this.#temperature = value; }
	setMaxTokens(value: number | undefined) { this.#maxOutputTokens = value; }
	setTopP(value: number | undefined) { this.#topP = value; }
	setAdditionalSystemInstructions(value: string | undefined) {
		this.#additionalSystemInstructions = value;
	}
	setPermissionOverride(key: string, value: boolean | undefined) {
		if (value === undefined) {
			const { [key]: _, ...rest } = this.#permissionOverrides;
			this.#permissionOverrides = rest;
		} else {
			this.#permissionOverrides = { ...this.#permissionOverrides, [key]: value };
		}
	}
	setSkipTools(value: boolean | undefined) { this.#skipTools = value; }
	setRunTimeout(value: string | undefined) { this.#runTimeout = value; }

	reset() {
		this.#providerKey = undefined;
		this.#modelId = undefined;
		this.#additionalSystemInstructions = undefined;
		this.#temperature = undefined;
		this.#maxOutputTokens = undefined;
		this.#topP = undefined;
		this.#permissionOverrides = {};
		this.#skipTools = undefined;
		this.#runTimeout = undefined;
	}
}

// ============================================
// ModelSelector child state
// ============================================

const ModelSelectorContext = new Context<RunConfigModelSelectorState>('RunConfig.ModelSelector');

interface ModelSelectorOpts {
	runConfig: ReadableBox<RunConfigState>;
	providers: ReadableBox<ProviderOption[]>;
	disabled: ReadableBox<boolean>;
}

export class RunConfigModelSelectorState {
	readonly #opts: ModelSelectorOpts;

	constructor(opts: ModelSelectorOpts) {
		this.#opts = opts;
	}

	static create(opts: ModelSelectorOpts) {
		return ModelSelectorContext.set(new RunConfigModelSelectorState(opts));
	}

	static get() { return ModelSelectorContext.get(); }

	get providerKey() { return this.#opts.runConfig.current.providerKey; }
	get modelId() { return this.#opts.runConfig.current.modelId; }
	get providers() { return this.#opts.providers.current; }
	get disabled() { return this.#opts.disabled.current; }

	readonly setModel = (providerKey: string | undefined, modelId: string | undefined) => {
		this.#opts.runConfig.current.setModel(providerKey, modelId);
	};

	get props(): RunConfigModelSelectorHTMLProps {
		return {
			'data-run-config-model': '',
			'data-disabled': boolToEmptyStrOrUndef(this.disabled),
		};
	}

	get snippetProps(): RunConfigModelSelectorSnippetProps {
		return {
			providerKey: this.providerKey,
			modelId: this.modelId,
			providers: this.providers,
			disabled: this.disabled,
			setModel: this.setModel,
		};
	}
}

// ============================================
// TemperatureSlider child state
// ============================================

const TemperatureSliderContext = new Context<RunConfigTemperatureSliderState>(
	'RunConfig.TemperatureSlider'
);

interface TemperatureSliderOpts {
	runConfig: ReadableBox<RunConfigState>;
	min: ReadableBox<number>;
	max: ReadableBox<number>;
	step: ReadableBox<number>;
	disabled: ReadableBox<boolean>;
}

export class RunConfigTemperatureSliderState {
	readonly #opts: TemperatureSliderOpts;

	constructor(opts: TemperatureSliderOpts) {
		this.#opts = opts;
	}

	static create(opts: TemperatureSliderOpts) {
		return TemperatureSliderContext.set(new RunConfigTemperatureSliderState(opts));
	}

	static get() { return TemperatureSliderContext.get(); }

	get value() { return this.#opts.runConfig.current.temperature; }
	get min() { return this.#opts.min.current; }
	get max() { return this.#opts.max.current; }
	get step() { return this.#opts.step.current; }
	get disabled() { return this.#opts.disabled.current; }

	readonly setValue = (value: number | undefined) => {
		this.#opts.runConfig.current.setTemperature(value);
	};

	get props(): RunConfigTemperatureSliderHTMLProps {
		return {
			'data-run-config-temperature': '',
			'data-disabled': boolToEmptyStrOrUndef(this.disabled),
		};
	}

	get snippetProps(): RunConfigTemperatureSliderSnippetProps {
		return {
			value: this.value,
			min: this.min,
			max: this.max,
			step: this.step,
			disabled: this.disabled,
			setValue: this.setValue,
		};
	}
}

// ============================================
// TopPSlider child state
// ============================================

const TopPSliderContext = new Context<RunConfigTopPSliderState>('RunConfig.TopPSlider');

interface TopPSliderOpts {
	runConfig: ReadableBox<RunConfigState>;
	min: ReadableBox<number>;
	max: ReadableBox<number>;
	step: ReadableBox<number>;
	disabled: ReadableBox<boolean>;
}

export class RunConfigTopPSliderState {
	readonly #opts: TopPSliderOpts;

	constructor(opts: TopPSliderOpts) {
		this.#opts = opts;
	}

	static create(opts: TopPSliderOpts) {
		return TopPSliderContext.set(new RunConfigTopPSliderState(opts));
	}

	static get() { return TopPSliderContext.get(); }

	get value() { return this.#opts.runConfig.current.topP; }
	get min() { return this.#opts.min.current; }
	get max() { return this.#opts.max.current; }
	get step() { return this.#opts.step.current; }
	get disabled() { return this.#opts.disabled.current; }

	readonly setValue = (value: number | undefined) => {
		this.#opts.runConfig.current.setTopP(value);
	};

	get props(): RunConfigTopPSliderHTMLProps {
		return {
			'data-run-config-top-p': '',
			'data-disabled': boolToEmptyStrOrUndef(this.disabled),
		};
	}

	get snippetProps(): RunConfigTopPSliderSnippetProps {
		return {
			value: this.value,
			min: this.min,
			max: this.max,
			step: this.step,
			disabled: this.disabled,
			setValue: this.setValue,
		};
	}
}

// ============================================
// MaxTokensInput child state
// ============================================

const MaxTokensInputContext = new Context<RunConfigMaxTokensInputState>('RunConfig.MaxTokensInput');

interface MaxTokensInputOpts {
	runConfig: ReadableBox<RunConfigState>;
	min: ReadableBox<number>;
	max: ReadableBox<number | undefined>;
	disabled: ReadableBox<boolean>;
}

export class RunConfigMaxTokensInputState {
	readonly #opts: MaxTokensInputOpts;

	constructor(opts: MaxTokensInputOpts) {
		this.#opts = opts;
	}

	static create(opts: MaxTokensInputOpts) {
		return MaxTokensInputContext.set(new RunConfigMaxTokensInputState(opts));
	}

	static get() { return MaxTokensInputContext.get(); }

	get value() { return this.#opts.runConfig.current.maxOutputTokens; }
	get min() { return this.#opts.min.current; }
	get max() { return this.#opts.max.current; }
	get disabled() { return this.#opts.disabled.current; }

	readonly setValue = (value: number | undefined) => {
		this.#opts.runConfig.current.setMaxTokens(value);
	};

	get props(): RunConfigMaxTokensInputHTMLProps {
		return {
			'data-run-config-max-tokens': '',
			'data-disabled': boolToEmptyStrOrUndef(this.disabled),
		};
	}

	get snippetProps(): RunConfigMaxTokensInputSnippetProps {
		return {
			value: this.value,
			min: this.min,
			max: this.max,
			disabled: this.disabled,
			setValue: this.setValue,
		};
	}
}

// ============================================
// SystemInstructionsInput child state
// ============================================

const SystemInstructionsInputContext = new Context<RunConfigSystemInstructionsInputState>(
	'RunConfig.SystemInstructionsInput'
);

interface SystemInstructionsInputOpts {
	runConfig: ReadableBox<RunConfigState>;
	disabled: ReadableBox<boolean>;
}

export class RunConfigSystemInstructionsInputState {
	readonly #opts: SystemInstructionsInputOpts;

	constructor(opts: SystemInstructionsInputOpts) {
		this.#opts = opts;
	}

	static create(opts: SystemInstructionsInputOpts) {
		return SystemInstructionsInputContext.set(
			new RunConfigSystemInstructionsInputState(opts)
		);
	}

	static get() { return SystemInstructionsInputContext.get(); }

	get value() { return this.#opts.runConfig.current.additionalSystemInstructions; }
	get disabled() { return this.#opts.disabled.current; }

	readonly setValue = (value: string | undefined) => {
		this.#opts.runConfig.current.setAdditionalSystemInstructions(value);
	};

	get props(): RunConfigSystemInstructionsInputHTMLProps {
		return {
			'data-run-config-system-instructions': '',
			'data-disabled': boolToEmptyStrOrUndef(this.disabled),
		};
	}

	get snippetProps(): RunConfigSystemInstructionsInputSnippetProps {
		return {
			value: this.value,
			disabled: this.disabled,
			setValue: this.setValue,
		};
	}
}

// ============================================
// PermissionOverridesPanel child state
// ============================================

const PermissionOverridesPanelContext = new Context<RunConfigPermissionOverridesPanelState>(
	'RunConfig.PermissionOverridesPanel'
);

interface PermissionOverridesPanelOpts {
	runConfig: ReadableBox<RunConfigState>;
	permissions: ReadableBox<string[]>;
	disabled: ReadableBox<boolean>;
}

export class RunConfigPermissionOverridesPanelState {
	readonly #opts: PermissionOverridesPanelOpts;

	constructor(opts: PermissionOverridesPanelOpts) {
		this.#opts = opts;
	}

	static create(opts: PermissionOverridesPanelOpts) {
		return PermissionOverridesPanelContext.set(
			new RunConfigPermissionOverridesPanelState(opts)
		);
	}

	static get() { return PermissionOverridesPanelContext.get(); }

	get items(): PermissionOverrideItem[] {
		const overrides = this.#opts.runConfig.current.permissionOverrides;
		return this.#opts.permissions.current.map((key) => ({
			key,
			label: key,
			value: overrides[key],
		}));
	}

	get disabled() { return this.#opts.disabled.current; }

	readonly setOverride = (key: string, value: boolean | undefined) => {
		this.#opts.runConfig.current.setPermissionOverride(key, value);
	};

	get props(): RunConfigPermissionOverridesPanelHTMLProps {
		return {
			'data-run-config-permission-overrides': '',
			'data-disabled': boolToEmptyStrOrUndef(this.disabled),
		};
	}

	get snippetProps(): RunConfigPermissionOverridesPanelSnippetProps {
		return {
			items: this.items,
			disabled: this.disabled,
			setOverride: this.setOverride,
		};
	}
}

// ============================================
// SkipToolsToggle child state
// ============================================

const SkipToolsToggleContext = new Context<RunConfigSkipToolsToggleState>(
	'RunConfig.SkipToolsToggle'
);

interface SkipToolsToggleOpts {
	runConfig: ReadableBox<RunConfigState>;
	disabled: ReadableBox<boolean>;
}

export class RunConfigSkipToolsToggleState {
	readonly #opts: SkipToolsToggleOpts;

	constructor(opts: SkipToolsToggleOpts) {
		this.#opts = opts;
	}

	static create(opts: SkipToolsToggleOpts) {
		return SkipToolsToggleContext.set(new RunConfigSkipToolsToggleState(opts));
	}

	static get() { return SkipToolsToggleContext.get(); }

	get value() { return this.#opts.runConfig.current.skipTools; }
	get disabled() { return this.#opts.disabled.current; }

	readonly setValue = (value: boolean | undefined) => {
		this.#opts.runConfig.current.setSkipTools(value);
	};

	get props(): RunConfigSkipToolsToggleHTMLProps {
		return {
			'data-run-config-skip-tools': '',
			'data-disabled': boolToEmptyStrOrUndef(this.disabled),
			'data-checked': boolToEmptyStrOrUndef(this.value ?? false),
		};
	}

	get snippetProps(): RunConfigSkipToolsToggleSnippetProps {
		return {
			value: this.value,
			disabled: this.disabled,
			setValue: this.setValue,
		};
	}
}

// ============================================
// RunTimeoutInput child state
// ============================================

const RunTimeoutInputContext = new Context<RunConfigRunTimeoutInputState>(
	'RunConfig.RunTimeoutInput'
);

interface RunTimeoutInputOpts {
	runConfig: ReadableBox<RunConfigState>;
	disabled: ReadableBox<boolean>;
}

export class RunConfigRunTimeoutInputState {
	readonly #opts: RunTimeoutInputOpts;

	constructor(opts: RunTimeoutInputOpts) {
		this.#opts = opts;
	}

	static create(opts: RunTimeoutInputOpts) {
		return RunTimeoutInputContext.set(new RunConfigRunTimeoutInputState(opts));
	}

	static get() { return RunTimeoutInputContext.get(); }

	get value() { return this.#opts.runConfig.current.runTimeout; }
	get disabled() { return this.#opts.disabled.current; }

	readonly setValue = (value: string | undefined) => {
		this.#opts.runConfig.current.setRunTimeout(value);
	};

	get props(): RunConfigRunTimeoutInputHTMLProps {
		return {
			'data-run-config-run-timeout': '',
			'data-disabled': boolToEmptyStrOrUndef(this.disabled),
		};
	}

	get snippetProps(): RunConfigRunTimeoutInputSnippetProps {
		return {
			value: this.value,
			disabled: this.disabled,
			setValue: this.setValue,
		};
	}
}
