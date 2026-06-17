import { Context } from 'runed';
import { type ReadableBox } from 'svelte-toolbelt';
import { boolToEmptyStrOrUndef } from '$lib/internal/attrs.js';
import type {
	AgentClientInput,
	AgentModelTransportMode,
	AgentRunClientConfig,
	AudioRunConfig,
	CompactionBehavior,
	RunConfig,
	ChatRunConfig,
	UploadStrategy,
} from '@hpd-research/hpd-agent-client';
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
	#modelTransport = $state<AgentModelTransportMode | undefined>(undefined);
	#clients = $state<AgentRunClientConfig | undefined>(undefined);
	#providerKey = $state<string | undefined>(undefined);
	#modelId = $state<string | undefined>(undefined);
	#apiKey = $state<string | undefined>(undefined);
	#providerEndpoint = $state<string | undefined>(undefined);
	#customHeaders = $state<Record<string, string> | undefined>(undefined);
	#providerOptions = $state<Record<string, unknown> | undefined>(undefined);
	#systemInstructions = $state<string | undefined>(undefined);
	#additionalSystemInstructions = $state<string | undefined>(undefined);
	#temperature = $state<number | undefined>(undefined);
	#maxOutputTokens = $state<number | undefined>(undefined);
	#topP = $state<number | undefined>(undefined);
	#topK = $state<number | undefined>(undefined);
	#frequencyPenalty = $state<number | undefined>(undefined);
	#presencePenalty = $state<number | undefined>(undefined);
	#chatModelId = $state<string | undefined>(undefined);
	#stopSequences = $state<string[] | undefined>(undefined);
	#chatAdditionalProperties = $state<Record<string, unknown> | undefined>(undefined);
	#reasoning = $state<Record<string, unknown> | undefined>(undefined);
	#permissionOverrides = $state<Record<string, boolean>>({});
	#contextOverrides = $state<Record<string, unknown> | undefined>(undefined);
	#useCache = $state<boolean | undefined>(undefined);
	#coalesceDeltas = $state<boolean | undefined>(undefined);
	#skipTools = $state<boolean | undefined>(undefined);
	#runTimeout = $state<string | undefined>(undefined);
	#conversationIdOverride = $state<string | undefined>(undefined);
	#allowBackgroundResponses = $state<boolean | undefined>(undefined);
	#backgroundPollingInterval = $state<string | undefined>(undefined);
	#backgroundTimeout = $state<string | undefined>(undefined);
	#userMessage = $state<string | undefined>(undefined);
	#uploadStrategy = $state<UploadStrategy | undefined>(undefined);
	#audio = $state<AudioRunConfig | undefined>(undefined);
	#triggerCompaction = $state<boolean | undefined>(undefined);
	#skipCompaction = $state<boolean | undefined>(undefined);
	#compactionBehaviorOverride = $state<CompactionBehavior | undefined>(undefined);
	#structuredOutput = $state<Record<string, unknown> | undefined>(undefined);
	#clientToolInput = $state<AgentClientInput | undefined>(undefined);

	// Plain getters — reactive because they read $state fields
	get modelTransport(): AgentModelTransportMode | undefined { return this.#modelTransport; }
	get clients(): AgentRunClientConfig | undefined { return this.#clients; }
	get providerKey() { return this.#providerKey; }
	get modelId() { return this.#modelId; }
	get apiKey() { return this.#apiKey; }
	get providerEndpoint() { return this.#providerEndpoint; }
	get customHeaders() { return this.#customHeaders; }
	get providerOptions() { return this.#providerOptions; }
	get systemInstructions() { return this.#systemInstructions; }
	get temperature() { return this.#temperature; }
	get maxOutputTokens() { return this.#maxOutputTokens; }
	get topP() { return this.#topP; }
	get topK() { return this.#topK; }
	get frequencyPenalty() { return this.#frequencyPenalty; }
	get presencePenalty() { return this.#presencePenalty; }
	get chatModelId() { return this.#chatModelId; }
	get stopSequences() { return this.#stopSequences; }
	get chatAdditionalProperties() { return this.#chatAdditionalProperties; }
	get reasoning() { return this.#reasoning; }
	get additionalSystemInstructions() { return this.#additionalSystemInstructions; }
	get contextOverrides() { return this.#contextOverrides; }
	get useCache() { return this.#useCache; }
	get coalesceDeltas() { return this.#coalesceDeltas; }
	get skipTools() { return this.#skipTools; }
	get runTimeout() { return this.#runTimeout; }
	get conversationIdOverride() { return this.#conversationIdOverride; }
	get allowBackgroundResponses() { return this.#allowBackgroundResponses; }
	get backgroundPollingInterval() { return this.#backgroundPollingInterval; }
	get backgroundTimeout() { return this.#backgroundTimeout; }
	get userMessage() { return this.#userMessage; }
	get uploadStrategy(): UploadStrategy | undefined { return this.#uploadStrategy; }
	get audio(): AudioRunConfig | undefined { return this.#audio; }
	get triggerCompaction() { return this.#triggerCompaction; }
	get skipCompaction() { return this.#skipCompaction; }
	get compactionBehaviorOverride(): CompactionBehavior | undefined { return this.#compactionBehaviorOverride; }
	get structuredOutput() { return this.#structuredOutput; }
	get clientToolInput(): AgentClientInput | undefined { return this.#clientToolInput; }
	get permissionOverrides(): Readonly<Record<string, boolean>> {
		return this.#permissionOverrides;
	}

	// Collapses chat sub-object — undefined when all chat fields are unset
	get chat(): ChatRunConfig | undefined {
		const {
			temperature,
			maxOutputTokens,
			topP,
			topK,
			frequencyPenalty,
			presencePenalty,
			chatModelId,
			stopSequences,
			chatAdditionalProperties,
			reasoning,
		} = this;
		if (
			temperature === undefined &&
			maxOutputTokens === undefined &&
			topP === undefined &&
			topK === undefined &&
			frequencyPenalty === undefined &&
			presencePenalty === undefined &&
			chatModelId === undefined &&
			stopSequences === undefined &&
			chatAdditionalProperties === undefined &&
			reasoning === undefined
		)
			return undefined;
		return {
			...(temperature !== undefined && { temperature }),
			...(maxOutputTokens !== undefined && { maxOutputTokens }),
			...(topP !== undefined && { topP }),
			...(topK !== undefined && { topK }),
			...(frequencyPenalty !== undefined && { frequencyPenalty }),
			...(presencePenalty !== undefined && { presencePenalty }),
			...(chatModelId !== undefined && { modelId: chatModelId }),
			...(stopSequences !== undefined && { stopSequences }),
			...(chatAdditionalProperties !== undefined && { additionalProperties: chatAdditionalProperties }),
			...(reasoning !== undefined && { reasoning }),
		};
	}

	// Final value handed to send() — undefined when nothing is set
	get value(): RunConfig | undefined {
		const {
			modelTransport,
			clients,
			providerKey,
			modelId,
			apiKey,
			providerEndpoint,
			customHeaders,
			providerOptions,
			systemInstructions,
			additionalSystemInstructions,
			chat,
			contextOverrides,
			useCache,
			coalesceDeltas,
			skipTools,
			runTimeout,
			conversationIdOverride,
			allowBackgroundResponses,
			backgroundPollingInterval,
			backgroundTimeout,
			userMessage,
			uploadStrategy,
			audio,
			triggerCompaction,
			skipCompaction,
			compactionBehaviorOverride,
			structuredOutput,
			clientToolInput,
		} = this;
		const permissionOverrides =
			Object.keys(this.#permissionOverrides).length > 0
				? this.#permissionOverrides
				: undefined;
		if (
			modelTransport === undefined &&
			clients === undefined &&
			providerKey === undefined &&
			modelId === undefined &&
			apiKey === undefined &&
			providerEndpoint === undefined &&
			customHeaders === undefined &&
			providerOptions === undefined &&
			systemInstructions === undefined &&
			additionalSystemInstructions === undefined &&
			chat === undefined &&
			permissionOverrides === undefined &&
			contextOverrides === undefined &&
			useCache === undefined &&
			coalesceDeltas === undefined &&
			skipTools === undefined &&
			runTimeout === undefined &&
			conversationIdOverride === undefined &&
			allowBackgroundResponses === undefined &&
			backgroundPollingInterval === undefined &&
			backgroundTimeout === undefined &&
			userMessage === undefined &&
			uploadStrategy === undefined &&
			audio === undefined &&
			triggerCompaction === undefined &&
			skipCompaction === undefined &&
			compactionBehaviorOverride === undefined &&
			structuredOutput === undefined &&
			clientToolInput === undefined
		)
			return undefined;
		return {
			...(modelTransport !== undefined && { modelTransport }),
			...(clients !== undefined && { clients }),
			...(providerKey !== undefined && { providerKey }),
			...(modelId !== undefined && { modelId }),
			...(apiKey !== undefined && { apiKey }),
			...(providerEndpoint !== undefined && { providerEndpoint }),
			...(customHeaders !== undefined && { customHeaders }),
			...(providerOptions !== undefined && { providerOptions }),
			...(systemInstructions !== undefined && { systemInstructions }),
			...(additionalSystemInstructions !== undefined && { additionalSystemInstructions }),
			...(chat !== undefined && { chat }),
			...(permissionOverrides !== undefined && { permissionOverrides }),
			...(contextOverrides !== undefined && { contextOverrides }),
			...(useCache !== undefined && { useCache }),
			...(coalesceDeltas !== undefined && { coalesceDeltas }),
			...(skipTools !== undefined && { skipTools }),
			...(runTimeout !== undefined && { runTimeout }),
			...(conversationIdOverride !== undefined && { conversationIdOverride }),
			...(allowBackgroundResponses !== undefined && { allowBackgroundResponses }),
			...(backgroundPollingInterval !== undefined && { backgroundPollingInterval }),
			...(backgroundTimeout !== undefined && { backgroundTimeout }),
			...(userMessage !== undefined && { userMessage }),
			...(uploadStrategy !== undefined && { uploadStrategy }),
			...(audio !== undefined && { audio }),
			...(triggerCompaction !== undefined && { triggerCompaction }),
			...(skipCompaction !== undefined && { skipCompaction }),
			...(compactionBehaviorOverride !== undefined && { compactionBehaviorOverride }),
			...(structuredOutput !== undefined && { structuredOutput }),
			...(clientToolInput !== undefined && { clientToolInput }),
		};
	}

	// Setters
	setModel(providerKey: string | undefined, modelId: string | undefined) {
		this.#providerKey = providerKey;
		this.#modelId = modelId;
	}
	setModelTransport(value: AgentModelTransportMode | undefined) { this.#modelTransport = value; }
	setClients(value: AgentRunClientConfig | undefined) { this.#clients = value; }
	setApiKey(value: string | undefined) { this.#apiKey = value; }
	setProviderEndpoint(value: string | undefined) { this.#providerEndpoint = value; }
	setCustomHeaders(value: Record<string, string> | undefined) { this.#customHeaders = value; }
	setProviderOptions(value: Record<string, unknown> | undefined) { this.#providerOptions = value; }
	setSystemInstructions(value: string | undefined) { this.#systemInstructions = value; }
	setTemperature(value: number | undefined) { this.#temperature = value; }
	setMaxTokens(value: number | undefined) { this.#maxOutputTokens = value; }
	setTopP(value: number | undefined) { this.#topP = value; }
	setTopK(value: number | undefined) { this.#topK = value; }
	setFrequencyPenalty(value: number | undefined) { this.#frequencyPenalty = value; }
	setPresencePenalty(value: number | undefined) { this.#presencePenalty = value; }
	setChatModelId(value: string | undefined) { this.#chatModelId = value; }
	setStopSequences(value: string[] | undefined) { this.#stopSequences = value; }
	setChatAdditionalProperties(value: Record<string, unknown> | undefined) { this.#chatAdditionalProperties = value; }
	setReasoning(value: Record<string, unknown> | undefined) { this.#reasoning = value; }
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
	setContextOverrides(value: Record<string, unknown> | undefined) { this.#contextOverrides = value; }
	setUseCache(value: boolean | undefined) { this.#useCache = value; }
	setCoalesceDeltas(value: boolean | undefined) { this.#coalesceDeltas = value; }
	setSkipTools(value: boolean | undefined) { this.#skipTools = value; }
	setRunTimeout(value: string | undefined) { this.#runTimeout = value; }
	setConversationIdOverride(value: string | undefined) { this.#conversationIdOverride = value; }
	setAllowBackgroundResponses(value: boolean | undefined) { this.#allowBackgroundResponses = value; }
	setBackgroundPollingInterval(value: string | undefined) { this.#backgroundPollingInterval = value; }
	setBackgroundTimeout(value: string | undefined) { this.#backgroundTimeout = value; }
	setUserMessage(value: string | undefined) { this.#userMessage = value; }
	setUploadStrategy(value: UploadStrategy | undefined) { this.#uploadStrategy = value; }
	setAudio(value: AudioRunConfig | undefined) { this.#audio = value; }
	setTriggerCompaction(value: boolean | undefined) { this.#triggerCompaction = value; }
	setSkipCompaction(value: boolean | undefined) { this.#skipCompaction = value; }
	setCompactionBehaviorOverride(value: CompactionBehavior | undefined) { this.#compactionBehaviorOverride = value; }
	setStructuredOutput(value: Record<string, unknown> | undefined) { this.#structuredOutput = value; }
	setClientToolInput(value: AgentClientInput | undefined) { this.#clientToolInput = value; }

	reset() {
		this.#modelTransport = undefined;
		this.#clients = undefined;
		this.#providerKey = undefined;
		this.#modelId = undefined;
		this.#apiKey = undefined;
		this.#providerEndpoint = undefined;
		this.#customHeaders = undefined;
		this.#providerOptions = undefined;
		this.#systemInstructions = undefined;
		this.#additionalSystemInstructions = undefined;
		this.#temperature = undefined;
		this.#maxOutputTokens = undefined;
		this.#topP = undefined;
		this.#topK = undefined;
		this.#frequencyPenalty = undefined;
		this.#presencePenalty = undefined;
		this.#chatModelId = undefined;
		this.#stopSequences = undefined;
		this.#chatAdditionalProperties = undefined;
		this.#reasoning = undefined;
		this.#permissionOverrides = {};
		this.#contextOverrides = undefined;
		this.#useCache = undefined;
		this.#coalesceDeltas = undefined;
		this.#skipTools = undefined;
		this.#runTimeout = undefined;
		this.#conversationIdOverride = undefined;
		this.#allowBackgroundResponses = undefined;
		this.#backgroundPollingInterval = undefined;
		this.#backgroundTimeout = undefined;
		this.#userMessage = undefined;
		this.#uploadStrategy = undefined;
		this.#audio = undefined;
		this.#triggerCompaction = undefined;
		this.#skipCompaction = undefined;
		this.#compactionBehaviorOverride = undefined;
		this.#structuredOutput = undefined;
		this.#clientToolInput = undefined;
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
