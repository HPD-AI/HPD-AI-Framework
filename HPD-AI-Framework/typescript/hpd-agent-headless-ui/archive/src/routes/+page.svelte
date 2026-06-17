<script lang="ts">
	import { onMount } from 'svelte';
	import {
		ChatInput,
		Message,
		MessageActions,
		MessageEdit,
		MessageList,
		PermissionDialog,
		RunConfig,
		RunConfigState,
		SessionList,
		ToolExecution,
		createWorkspace,
		type Workspace
	} from '$lib/index.js';
	import type { Message as AgentMessage, ToolCall } from '$lib/agent/types.js';
	import type { RunConfig as AgentRunConfig } from '$lib/workspace/types.js';
	import type {
		AgentModelTransportMode,
		CompactionBehavior,
		Session,
		UploadStrategy
	} from '@hpd-research/hpd-agent-client';

	type WorkspaceRoot = {
		id?: string;
		label?: string;
		path: string;
	};

	type WorkspaceProfile = {
		key: string;
		name: string;
		roots?: WorkspaceRoot[];
	};

	type WorkspaceRunContext = {
		version: number;
		defaultRootId?: string;
		defaultRootPath: string;
		roots: Array<{
			id?: string;
			path: string;
			label: string | null;
		}>;
	};

	type RuntimeProvider = {
		key: string;
		displayName?: string;
		ready?: boolean;
		model?: string;
	};

	type RuntimeDetails = {
		provider?: string;
		model?: string;
		providers?: RuntimeProvider[];
	};

	type ProviderOption = {
		key: string;
		label: string;
		models: { id: string; label: string }[];
	};

	type ModelRecord = {
		name?: string;
		Name?: string;
		status?: string;
		Status?: string;
		family?: string;
		Family?: string;
		tool_call?: boolean;
		ToolCall?: boolean;
		modalities?: { output?: string[] };
		Modalities?: { Output?: string[] };
	};

	type ModelsDatabase = {
		providers?: Record<string, { models?: Record<string, ModelRecord>; Models?: Record<string, ModelRecord> }>;
		Providers?: Record<string, { models?: Record<string, ModelRecord>; Models?: Record<string, ModelRecord> }>;
	};

	const defaultAgentId = 'hpdos-agent';
	const runConfig = new RunConfigState();
	const permissionKeys = ['read_file', 'write_file', 'execute_command', 'write_workspace'];
	const explorationToolNames = new Set(['READFILE', 'GREP', 'GLOBSEARCH', 'LISTDIRECTORY']);
	const providerOptionsPlaceholder = '{"reasoningEffortLevel":"medium","webSearchEnabled":true}';
	const jsonObjectPlaceholder = '{"key":"value"}';
	const customHeadersPlaceholder = '{"ChatGPT-Account-Id":"account-id"}';
	const clientsPlaceholder = '{"chat":{"providerKey":"openai","modelName":"gpt-5.5"}}';
	const reasoningPlaceholder = '{"effort":"medium","output":"summary"}';
	const audioPlaceholder = '{"enabled":true,"voiceId":"alloy"}';
	const structuredOutputPlaceholder = '{"mode":"native"}';

	let workspace = $state<Workspace | null>(null);
	let workspaceProfiles = $state<WorkspaceProfile[]>([]);
	let activeWorkspaceKey = $state('');
	let activeWorkspace = $state<WorkspaceProfile | null>(null);
	let runtimeDetails = $state<RuntimeDetails>({});
	let modelProviders = $state<ProviderOption[]>([]);
	let loadError = $state<string | null>(null);
	let settingsOpen = $state(false);
	let sidebarCollapsed = $state(false);
	let transcriptCollapsed = $state(false);
	let editingIndex = $state<number | null>(null);
	let composerValue = $state('');
	let workspaceModalOpen = $state(false);
	let workspaceFormMode = $state<'create' | 'edit'>('create');
	let workspaceFormKey = $state('');
	let workspaceFormName = $state('');
	let workspaceFormRoots = $state<WorkspaceRoot[]>([]);
	let providerApiKey = $state('');
	let providerEndpoint = $state('');
	let providerOptionsText = $state('');
	let customHeadersText = $state('');
	let clientsText = $state('');
	let contextOverridesText = $state('');
	let chatAdditionalPropertiesText = $state('');
	let reasoningText = $state('');
	let stopSequencesText = $state('');
	let clientToolInputText = $state('');
	let audioText = $state('');
	let structuredOutputText = $state('');

	const activeSession = $derived.by(() => {
		const currentWorkspace = workspace;
		if (!currentWorkspace?.activeSessionId) return null;
		return currentWorkspace.sessions.find((session) => session.id === currentWorkspace.activeSessionId) ?? null;
	});
	const activeSessionWorkspaceKey = $derived.by(() => {
		const value = activeSession?.metadata?.workspaceKey;
		return typeof value === 'string' ? value : '';
	});
	const activeSessionMatchesWorkspace = $derived.by(() => {
		if (!activeSession) return false;
		if (!activeWorkspaceKey) return true;
		return activeSessionWorkspaceKey === activeWorkspaceKey;
	});
	const messages = $derived.by(() => activeSessionMatchesWorkspace ? workspace?.state?.messages ?? [] : []);
	const isStreaming = $derived.by(() => activeSessionMatchesWorkspace ? workspace?.state?.streaming ?? false : false);
	const isReasoning = $derived.by(() => activeSessionMatchesWorkspace ? workspace?.state?.reasoning ?? false : false);
	const activeTools = $derived.by(() => activeSessionMatchesWorkspace ? workspace?.state?.activeTools ?? [] : []);
	const pendingPermissions = $derived.by(() => activeSessionMatchesWorkspace ? workspace?.state?.pendingPermissions ?? [] : []);
	const pendingClarifications = $derived.by(() => activeSessionMatchesWorkspace ? workspace?.state?.pendingClarifications ?? [] : []);
	const activeBranch = $derived(workspace?.activeBranch ?? null);
	const canSend = $derived(Boolean(workspace && activeWorkspace && activeSessionMatchesWorkspace && (workspace.state?.canSend ?? !isStreaming)));
	const startupContext = readStartupContext();
	const allSessions = $derived.by(() => {
		if (!workspace) return [];
		return [...workspace.sessions].sort((a, b) => getSessionActivityTimestamp(b) - getSessionActivityTimestamp(a));
	});
	const workspaceSessions = $derived.by(() => {
		if (!workspace || !activeWorkspaceKey) return [];
		return allSessions.filter((session) => session.metadata?.workspaceKey === activeWorkspaceKey);
	});
	const activeRootSummary = $derived.by(() => {
		const roots = activeWorkspace?.roots ?? [];
		if (roots.length === 0) return 'No roots';
		if (roots.length === 1) return roots[0].path;
		return `${roots[0].path} + ${roots.length - 1} more`;
	});
	const monitorStatus = $derived.by(() => {
		if (!workspace) return 'Connecting';
		if (!activeWorkspace) return 'No workspace selected';
		if (!activeSessionMatchesWorkspace) return 'No active workspace session';
		if (pendingPermissions.length > 0) return 'Awaiting permission';
		if (pendingClarifications.length > 0) return 'Awaiting clarification';
		if (activeTools.length > 0) return 'Running tools';
		if (isReasoning) return 'Thinking';
		if (isStreaming) return 'Agent processing';
		return 'Ready';
	});
	const monitorItems = $derived.by(() => buildMonitorItems());

	function resolveBackendUrl() {
		const origin = globalThis.location?.origin ?? '';
		if (origin.includes('localhost') || origin.includes('127.0.0.1')) return origin;
		return 'http://127.0.0.1:4317';
	}

	const backendUrl = resolveBackendUrl();

	onMount(async () => {
		try {
			await Promise.all([loadRuntime(), loadWorkspaces(startupContext.workspaceKey)]);

			workspace = createWorkspaceInstance(startupContext.sessionId, startupContext.branchId);
		} catch (error) {
			loadError = error instanceof Error ? error.message : 'Could not initialize HPD-OS workspace.';
		}
	});

	function readStartupContext() {
		const params = new URLSearchParams(globalThis.location?.search ?? '');
		return {
			workspaceKey: params.get('workspace') ?? '',
			sessionId: params.get('session') ?? '',
			branchId: params.get('branch') ?? 'main'
		};
	}

	function createWorkspaceInstance(sessionId?: string, branchId?: string) {
		return createWorkspace({
			baseUrl: backendUrl,
			agentId: defaultAgentId,
			sessionId: sessionId || undefined,
			initialBranchId: branchId || undefined,
			onError: (message) => (loadError = message)
		});
	}

	async function loadRuntime() {
		try {
			const runtimeResponse = await fetch(`${backendUrl}/api/hpdos/runtime`);
			if (runtimeResponse.ok) {
				runtimeDetails = await runtimeResponse.json();
			}
		} catch {
			runtimeDetails = {};
		}

		try {
			const modelsResponse = await fetch(`${backendUrl}/api/hpdos/modelsdev`);
			const modelsDb: ModelsDatabase | null = modelsResponse.ok
				? await modelsResponse.json()
				: await fetch(`${backendUrl}/api/hpdos/models`).then((res) => (res.ok ? res.json() : null));
			modelProviders = buildProviderOptions(runtimeDetails.providers ?? [], modelsDb);
		} catch {
			modelProviders = buildProviderOptions(runtimeDetails.providers ?? [], null);
		}

		const providerKey = runtimeDetails.provider ?? modelProviders[0]?.key;
		const modelId = runtimeDetails.model ?? modelProviders[0]?.models[0]?.id;
		if (providerKey && modelId) {
			runConfig.setModel(providerKey, modelId);
		}
		runConfig.setTemperature(0.7);
	}

	async function loadWorkspaces(preferredKey = activeWorkspaceKey) {
		const response = await fetch(`${backendUrl}/api/hpdos/workspaces`);
		if (!response.ok) throw new Error('Could not load workspace profiles.');

		workspaceProfiles = await response.json();
		activeWorkspaceKey = workspaceProfiles.some((item) => item.key === preferredKey)
			? preferredKey
			: workspaceProfiles[0]?.key ?? '';
		activeWorkspace = workspaceProfiles.find((item) => item.key === activeWorkspaceKey) ?? null;
	}

	function buildProviderOptions(providers: RuntimeProvider[], modelsDb: ModelsDatabase | null): ProviderOption[] {
		const advertised = providers.length
			? providers
			: [
					{ key: 'openrouter', displayName: 'OpenRouter', ready: true },
					{ key: 'openai', displayName: 'OpenAI', ready: false },
					{ key: 'anthropic', displayName: 'Anthropic', ready: false }
				];
		const dbProviders = modelsDb?.providers ?? modelsDb?.Providers ?? {};

		return advertised.map((provider) => {
			const providerData = dbProviders[getDatabaseProviderId(provider.key)];
			const rawModels = providerData?.models ?? providerData?.Models ?? {};
			const models = Object.entries(rawModels)
				.filter(([, model]) => isUsableChatModel(model))
				.map(([id, model]) => ({
					id,
					label: model.name ?? model.Name ?? id
				}))
				.sort((a, b) => a.label.localeCompare(b.label));

			return {
				key: provider.key,
				label: provider.displayName ?? provider.key,
				models: models.length ? models : provider.model ? [{ id: provider.model, label: provider.model }] : []
			};
		});
	}

	function isUsableChatModel(model: ModelRecord) {
		const name = (model.name ?? model.Name ?? '').toLowerCase();
		const family = (model.family ?? model.Family ?? '').toLowerCase();
		const status = model.status ?? model.Status;
		const output = model.modalities?.output ?? model.Modalities?.Output ?? ['text'];
		return status !== 'deprecated' && !name.includes('embed') && !family.includes('embed') && output.some((item) => item.toLowerCase() === 'text');
	}

	function getDatabaseProviderId(providerKey: string) {
		const mappings: Record<string, string> = {
			'azure-ai': 'azure',
			azure_ai: 'azure',
			'google-ai': 'google',
			google_ai: 'google',
			'hugging-face': 'huggingface',
			'onnx-runtime': 'onnxruntime',
			onnx: 'onnxruntime'
		};
		const normalized = providerKey.toLowerCase();
		return mappings[normalized] ?? normalized;
	}

	function buildWorkspaceContext(): WorkspaceRunContext | undefined {
		const roots = activeWorkspace?.roots ?? [];
		if (!activeWorkspace || roots.length === 0) return undefined;

		return {
			version: 1,
			defaultRootId: roots[0].id,
			defaultRootPath: roots[0].path,
			roots: roots.map((root) => ({
				id: root.id,
				path: root.path,
				label: root.label ?? null
			}))
		};
	}

	async function selectWorkspace(profile: WorkspaceProfile) {
		activeWorkspaceKey = profile.key;
		activeWorkspace = profile;

		const session = allSessions.find((item) => item.metadata?.workspaceKey === profile.key);
		if (session) await workspace?.selectSession(session.id);
	}

	async function selectSessionFromAll(session: Session) {
		const sessionWorkspaceKey = typeof session.metadata?.workspaceKey === 'string' ? session.metadata.workspaceKey : '';
		const matchingWorkspace = workspaceProfiles.find((profile) => profile.key === sessionWorkspaceKey) ?? null;

		activeWorkspaceKey = matchingWorkspace?.key ?? '';
		activeWorkspace = matchingWorkspace;
		await workspace?.selectSession(session.id);
	}

	function openWorkspaceModal(mode: 'create' | 'edit', profile: WorkspaceProfile | null = activeWorkspace) {
		workspaceFormMode = mode;
		workspaceFormKey = mode === 'edit' ? profile?.key ?? '' : '';
		workspaceFormName = mode === 'edit' ? profile?.name ?? '' : '';
		workspaceFormRoots = mode === 'edit' && profile?.roots?.length
			? profile.roots.map((root) => ({ ...root }))
			: [{ label: '', path: '' }];
		workspaceModalOpen = true;
	}

	function closeWorkspaceModal() {
		workspaceModalOpen = false;
	}

	function updateWorkspaceRoot(index: number, patch: Partial<WorkspaceRoot>) {
		workspaceFormRoots = workspaceFormRoots.map((root, rootIndex) => rootIndex === index ? { ...root, ...patch } : root);
	}

	function addWorkspaceRoot() {
		workspaceFormRoots = [...workspaceFormRoots, { label: '', path: '' }];
	}

	function removeWorkspaceRoot(index: number) {
		if (workspaceFormRoots.length <= 1) {
			alert('A workspace needs at least one directory root.');
			return;
		}
		workspaceFormRoots = workspaceFormRoots.filter((_, rootIndex) => rootIndex !== index);
	}

	async function browseWorkspaceRoot(index: number) {
		const root = workspaceFormRoots[index];
		try {
			const response = await fetch(`${backendUrl}/api/hpdos/workspaces/pick-folder`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({
					prompt: 'Choose a workspace folder',
					startPath: root?.path || null
				})
			});
			if (!response.ok) throw new Error(await response.text());
			const payload = await response.json();
			if (!payload.canceled && payload.path) {
				updateWorkspaceRoot(index, { path: payload.path });
			}
		} catch (error) {
			alert(error instanceof Error ? error.message : 'Could not open folder picker.');
		}
	}

	async function saveWorkspaceForm() {
		const name = workspaceFormName.trim();
		const roots = workspaceFormRoots.map((root) => ({
			...root,
			label: root.label?.trim() ?? '',
			path: root.path.trim()
		}));
		if (!name) {
			alert('Workspace name is required.');
			return;
		}
		if (roots.some((root) => !root.path)) {
			alert('Every workspace root needs an absolute path.');
			return;
		}

		const url = workspaceFormMode === 'edit'
			? `${backendUrl}/api/hpdos/workspaces/${encodeURIComponent(workspaceFormKey)}`
			: `${backendUrl}/api/hpdos/workspaces`;
		const method = workspaceFormMode === 'edit' ? 'PUT' : 'POST';
		try {
			const response = await fetch(url, {
				method,
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ key: workspaceFormKey, name, roots })
			});
			if (!response.ok) {
				const error = await response.json().catch(() => null);
				throw new Error(error?.error ?? 'Could not save workspace.');
			}
			const saved = await response.json();
			closeWorkspaceModal();
			await loadWorkspaces(saved.key);
		} catch (error) {
			alert(error instanceof Error ? error.message : 'Could not save workspace.');
		}
	}

	async function deleteWorkspace(profile: WorkspaceProfile) {
		if (!confirm(`Delete workspace "${profile.name}"?`)) return;
		try {
			const response = await fetch(`${backendUrl}/api/hpdos/workspaces/${encodeURIComponent(profile.key)}`, {
				method: 'DELETE'
			});
			if (!response.ok) throw new Error('Could not delete workspace.');
			const nextKey = activeWorkspaceKey === profile.key
				? workspaceProfiles.find((item) => item.key !== profile.key)?.key ?? ''
				: activeWorkspaceKey;
			await loadWorkspaces(nextKey);
		} catch (error) {
			alert(error instanceof Error ? error.message : 'Could not delete workspace.');
		}
	}

	async function createSessionForActiveWorkspace() {
		if (!workspace || !activeWorkspaceKey) return;
		await workspace.createSession({
			metadata: {
				workspaceKey: activeWorkspaceKey,
				name: 'New session'
			}
		});
	}

	async function deleteSession(sessionId: string) {
		if (!workspace) return;
		if (!confirm('Delete this session?')) return;
		await workspace.deleteSession(sessionId);
	}

	async function renameSession(session: Session) {
		if (!workspace) return;
		const nextName = prompt('Rename session', typeof session.metadata?.name === 'string' ? session.metadata.name : '');
		if (nextName === null) return;

		try {
			await workspace.client.updateSession(session.id, {
				metadata: {
					...(session.metadata ?? {}),
					name: nextName
				}
			});
			workspace = createWorkspaceInstance(workspace.activeSessionId ?? session.id, workspace.activeBranchId ?? 'main');
		} catch (error) {
			alert(error instanceof Error ? error.message : 'Could not rename session.');
		}
	}

	function getSessionActivityTimestamp(session: Session) {
		const value = session.lastActivity || session.createdAt;
		const timestamp = value ? new Date(value).getTime() : 0;
		return Number.isNaN(timestamp) ? 0 : timestamp;
	}

	async function sendMessage(value: string) {
		if (!workspace || !value.trim()) return;
		if (!activeSessionMatchesWorkspace) {
			await createSessionForActiveWorkspace();
		}

		const configError = validateRunConfigInputs();
		if (configError) {
			alert(configError);
			return;
		}

		await workspace.send(value, { runConfig: buildRunConfig() });
	}

	function buildRunConfig(): AgentRunConfig | undefined {
		const value: AgentRunConfig = { ...(runConfig.value ?? {}) };
		const workspaceContext = buildWorkspaceContext();
		if (workspaceContext) {
			value.contextOverrides = {
				...(value.contextOverrides ?? {}),
				workspace: workspaceContext
			};
		}

		return Object.keys(value).length > 0 ? value : undefined;
	}

	function parseJsonObject(raw: string) {
		const trimmed = raw.trim();
		if (!trimmed) return undefined;
		const parsed: unknown = JSON.parse(trimmed);
		if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
			throw new Error('Expected a JSON object.');
		}
		return parsed as Record<string, unknown>;
	}

	function parseStringRecord(raw: string) {
		const parsed = parseJsonObject(raw);
		if (!parsed) return undefined;
		for (const [key, value] of Object.entries(parsed)) {
			if (typeof value !== 'string') {
				throw new Error(`"${key}" must be a string.`);
			}
		}
		return parsed as Record<string, string>;
	}

	function parseStopSequences(raw: string) {
		if (!raw) return undefined;
		const values = raw.split('\n').map((item) => item.trim()).filter(Boolean);
		return values.length ? values : undefined;
	}

	function setJsonObjectField(raw: string, setter: (value: Record<string, unknown> | undefined) => void) {
		try {
			setter(parseJsonObject(raw));
		} catch {
			setter(undefined);
		}
	}

	function setStringRecordField(raw: string, setter: (value: Record<string, string> | undefined) => void) {
		try {
			setter(parseStringRecord(raw));
		} catch {
			setter(undefined);
		}
	}

	function validateRunConfigInputs() {
		const jsonFields: Array<[string, string, 'object' | 'stringRecord']> = [
			['Clients', clientsText, 'object'],
			['Custom headers', customHeadersText, 'stringRecord'],
			['Provider options', providerOptionsText, 'object'],
			['Context overrides', contextOverridesText, 'object'],
			['Chat additional properties', chatAdditionalPropertiesText, 'object'],
			['Reasoning', reasoningText, 'object'],
			['Client tool input', clientToolInputText, 'object'],
			['Audio', audioText, 'object'],
			['Structured output', structuredOutputText, 'object']
		];

		for (const [label, raw, kind] of jsonFields) {
			if (!raw.trim()) continue;
			try {
				kind === 'stringRecord' ? parseStringRecord(raw) : parseJsonObject(raw);
			} catch (error) {
				return `${label} must be valid JSON object syntax. ${error instanceof Error ? error.message : ''}`.trim();
			}
		}

		return null;
	}

	function formatSessionLabel(session: { id: string; metadata?: Record<string, unknown> }) {
		const name = typeof session.metadata?.name === 'string' ? session.metadata.name : '';
		return name || session.id.slice(0, 16);
	}

	function formatTime(message: AgentMessage) {
		return message.timestamp.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
	}

	function formatToolResult(toolCall: ToolCall) {
		if (toolCall.error) return toolCall.error;
		if (toolCall.resultText) return toolCall.resultText;
		if (toolCall.result) return typeof toolCall.result === 'string' ? toolCall.result : JSON.stringify(toolCall.result, null, 2);
		return '';
	}

	function normalizeToolName(toolName: string) {
		return toolName.replace(/[^a-z0-9]/gi, '').toUpperCase();
	}

	function isExplorationTool(toolCall: ToolCall) {
		return explorationToolNames.has(normalizeToolName(toolCall.name));
	}

	function explorationToolCalls(toolCalls: ToolCall[]) {
		return toolCalls.filter(isExplorationTool);
	}

	function regularToolCalls(toolCalls: ToolCall[]) {
		return toolCalls.filter((toolCall) => !isExplorationTool(toolCall));
	}

	function shortPathLabel(value: unknown) {
		if (typeof value !== 'string' || !value) return '?';
		const normalized = value.replace(/\\/g, '/').replace(/\/$/, '');
		const parts = normalized.split('/').filter(Boolean);
		return parts.at(-1) ?? normalized;
	}

	function countLabel(value: unknown, singular: string, plural: string) {
		const count = Number(value);
		if (value === null || value === undefined || value === '') return '';
		if (!Number.isFinite(count)) return ` ${value} ${plural}`;
		return count === 1 ? ` 1 ${singular}` : ` ${count} ${plural}`;
	}

	type ExplorationSummary = {
		kind: 'unknown' | 'read' | 'grep' | 'glob' | 'list';
		path?: string;
		pattern?: string;
		originalPattern?: string;
		totalResults?: string;
		totalMatches?: string;
		totalEntries?: string;
		recursive?: boolean;
		truncated?: boolean;
		hasMore?: boolean;
		unchanged?: boolean;
		isError?: boolean;
		errorMessage?: string;
	};

	function parseToolArgs(toolCall: ToolCall) {
		return toolCall.args ?? {};
	}

	function parseExplorationSummary(toolCall: ToolCall): ExplorationSummary {
		const text = formatToolResult(toolCall);
		if (!text.trim()) return { kind: 'unknown' };

		const root = parseXmlishRoot(text);
		if (!root) return parseJsonExplorationFallback(toolCall);

		if (root.name === 'error') {
			return {
				kind: 'unknown',
				path: root.attrs.path,
				isError: true,
				errorMessage: root.body.trim() || 'failed'
			};
		}

		const normalized = normalizeToolName(toolCall.name);
		if (normalized === 'READFILE') {
			return {
				kind: 'read',
				path: root.attrs.path,
				truncated: readBool(root.attrs.truncated),
				hasMore: hasChild(text, 'next_read'),
				unchanged: root.name === 'file_unchanged'
			};
		}

		if (normalized === 'GREP') {
			return {
				kind: 'grep',
				path: root.attrs.path,
				pattern: root.attrs.pattern,
				totalResults: root.attrs.total_results,
				totalMatches: root.attrs.total_matches,
				truncated: readBool(root.attrs.truncated),
				hasMore: hasChild(text, 'next_grep')
			};
		}

		if (normalized === 'GLOBSEARCH') {
			return {
				kind: 'glob',
				path: root.attrs.path ?? root.attrs.effective_path,
				pattern: root.attrs.pattern,
				originalPattern: root.attrs.original_pattern,
				totalMatches: root.attrs.total_matches,
				truncated: readBool(root.attrs.truncated),
				hasMore: hasChild(text, 'next_glob')
			};
		}

		if (normalized === 'LISTDIRECTORY') {
			return {
				kind: 'list',
				path: root.attrs.path,
				recursive: readBool(root.attrs.recursive),
				totalEntries: root.attrs.total_entries,
				truncated: readBool(root.attrs.truncated),
				hasMore: hasChild(text, 'next_list')
			};
		}

		return { kind: 'unknown' };
	}

	function parseJsonExplorationFallback(toolCall: ToolCall): ExplorationSummary {
		const text = formatToolResult(toolCall);
		let json: Record<string, unknown> | null = null;
		try {
			json = JSON.parse(text) as Record<string, unknown>;
		} catch {
			return { kind: 'unknown' };
		}

		const normalized = normalizeToolName(toolCall.name);
		const path = typeof json.path === 'string' ? json.path : undefined;
		const truncated = Boolean(json.truncated);
		const hasMore = Boolean(json.hasMore);
		if (normalized === 'READFILE') {
			return { kind: 'read', path, truncated, hasMore, unchanged: Boolean(json.unchanged) };
		}
		if (normalized === 'GREP') {
			return {
				kind: 'grep',
				path,
				pattern: typeof json.pattern === 'string' ? json.pattern : undefined,
				totalMatches: stringifyMaybe(json.totalMatches ?? json.totalResults),
				totalResults: stringifyMaybe(json.totalResults),
				truncated,
				hasMore
			};
		}
		if (normalized === 'GLOBSEARCH') {
			return {
				kind: 'glob',
				path,
				pattern: typeof json.pattern === 'string' ? json.pattern : undefined,
				originalPattern: typeof json.originalPattern === 'string' ? json.originalPattern : undefined,
				totalMatches: stringifyMaybe(json.totalMatches),
				truncated,
				hasMore
			};
		}
		if (normalized === 'LISTDIRECTORY') {
			return {
				kind: 'list',
				path,
				recursive: Boolean(json.recursive),
				totalEntries: stringifyMaybe(json.totalEntries),
				truncated,
				hasMore
			};
		}

		return { kind: 'unknown' };
	}

	function parseXmlishRoot(text: string) {
		const match = text.match(/<([A-Za-z_][\w:.-]*)([^>]*)>([\s\S]*)/) ?? text.match(/<([A-Za-z_][\w:.-]*)([^>]*)\/>/);
		if (!match) return null;
		return {
			name: match[1],
			attrs: parseXmlishAttrs(match[2] ?? ''),
			body: match[3] ?? ''
		};
	}

	function parseXmlishAttrs(raw: string): Record<string, string> {
		const attrs: Record<string, string> = {};
		for (const match of raw.matchAll(/([\w:.-]+)\s*=\s*("([^"]*)"|'([^']*)')/g)) {
			attrs[match[1]] = decodeXmlish(match[3] ?? match[4] ?? '');
		}
		return attrs;
	}

	function decodeXmlish(value: string) {
		return value
			.replace(/&quot;/g, '"')
			.replace(/&apos;/g, "'")
			.replace(/&lt;/g, '<')
			.replace(/&gt;/g, '>')
			.replace(/&amp;/g, '&');
	}

	function hasChild(text: string, childName: string) {
		return new RegExp(`<${childName}(\\s|>|/)`).test(text);
	}

	function readBool(value: unknown) {
		return typeof value === 'string' ? value.toLowerCase() === 'true' : Boolean(value);
	}

	function stringifyMaybe(value: unknown) {
		return value === null || value === undefined ? undefined : String(value);
	}

	function quotePattern(value: unknown) {
		const text = typeof value === 'string' && value.trim() ? value : '?';
		return `"${text}"`;
	}

	function shortScope(value: unknown) {
		if (typeof value !== 'string' || !value.trim()) return '.';
		const normalized = value.replace(/\\/g, '/');
		return normalized.length > 1 ? normalized.replace(/\/+$/, '') : normalized;
	}

	type MonitorItem = {
		id: string;
		label: string;
		detail: string;
		status: 'active' | 'waiting' | 'complete' | 'muted' | 'error';
	};

	function buildMonitorItems(): MonitorItem[] {
		const items: MonitorItem[] = [];

		if (!workspace) {
			return [{ id: 'workspace-loading', label: 'Workspace', detail: 'Connecting to runtime', status: 'active' }];
		}

		if (loadError) {
			items.push({ id: 'load-error', label: 'Error', detail: loadError, status: 'error' });
		}

		if (activeWorkspace) {
			items.push({
				id: `workspace-${activeWorkspace.key}`,
				label: 'Workspace',
				detail: activeRootSummary,
				status: activeSessionMatchesWorkspace ? 'complete' : 'waiting'
			});
		} else {
			items.push({ id: 'workspace-empty', label: 'Workspace', detail: 'Choose or create a workspace profile', status: 'waiting' });
		}

		if (activeSession) {
			items.push({
				id: `session-${activeSession.id}`,
				label: 'Session',
				detail: `${formatSessionLabel(activeSession)} / ${workspace?.activeBranchId ?? 'no branch'}`,
				status: activeSessionMatchesWorkspace ? 'complete' : 'waiting'
			});
		}

		for (const request of pendingPermissions) {
			items.push({
				id: `permission-${request.permissionId}`,
				label: 'Permission',
				detail: request.functionName || request.description || request.sourceName,
				status: 'waiting'
			});
		}

		for (const request of pendingClarifications) {
			items.push({
				id: `clarification-${request.requestId}`,
				label: 'Clarification',
				detail: request.question,
				status: 'waiting'
			});
		}

		if (isReasoning) {
			items.push({ id: 'reasoning', label: 'Reasoning', detail: 'Thought trace active', status: 'active' });
		}

		for (const toolCall of activeTools) {
			items.push({
				id: `tool-${toolCall.callId}`,
				label: isExplorationTool(toolCall) ? 'Exploration' : 'Tool',
				detail: summarizeExplorationTool(toolCall),
				status: toolCall.status === 'error' ? 'error' : 'active'
			});
		}

		const lastAssistant = [...messages].reverse().find((message) => message.role === 'assistant' && message.content.trim());
		if (lastAssistant) {
			items.push({
				id: `assistant-${lastAssistant.id}`,
				label: lastAssistant.streaming ? 'Assistant' : 'Last response',
				detail: compactText(lastAssistant.content),
				status: lastAssistant.streaming ? 'active' : 'muted'
			});
		}

		return items.slice(-8);
	}

	function compactText(value: string, maxLength = 140) {
		const compact = value.replace(/\s+/g, ' ').trim();
		if (compact.length <= maxLength) return compact;
		return `${compact.slice(0, maxLength - 1)}…`;
	}

	function summarizeExplorationTool(toolCall: ToolCall) {
		const normalized = normalizeToolName(toolCall.name);
		const args = parseToolArgs(toolCall);
		const summary = parseExplorationSummary(toolCall);

		if (summary.isError || toolCall.status === 'error') {
			return `${verbForExplorationTool(toolCall.name)} ${bestExplorationSubject(toolCall, summary)} failed`;
		}

		if (summary.kind === 'read' || normalized === 'READFILE') {
			return formatExplorationReadGroup([toolCall]);
		}

		if (summary.kind === 'grep' || normalized === 'GREP') {
			const pattern = summary.pattern ?? args.pattern;
			const scope = shortScope(summary.path ?? args.path);
			const text = scope ? `Search ${quotePattern(pattern)} in ${scope}` : `Search ${quotePattern(pattern)}`;
			return addExplorationMarkers(`${text}${countLabel(summary.totalMatches ?? summary.totalResults, 'match', 'matches')}`, summary);
		}

		if (summary.kind === 'glob' || normalized === 'GLOBSEARCH') {
			const pattern = summary.originalPattern ?? summary.pattern ?? args.pattern;
			const scope = shortScope(summary.path ?? args.path);
			const text = !scope || scope === '.'
				? `Find ${quotePattern(pattern)}`
				: `Find ${quotePattern(pattern)} in ${scope}`;
			return addExplorationMarkers(`${text}${countLabel(summary.totalMatches, 'match', 'matches')}`, summary);
		}

		if (summary.kind === 'list' || normalized === 'LISTDIRECTORY') {
			const scope = shortScope(summary.path ?? args.path);
			const recursive = summary.recursive || args.recursive ? ' recursively' : '';
			return addExplorationMarkers(`List ${scope}${recursive}${countLabel(summary.totalEntries, 'entry', 'entries')}`, summary);
		}

		return toolCall.name;
	}

	function summarizeExplorationGroup(toolCalls: ToolCall[]) {
		const activeCount = toolCalls.filter((toolCall) => toolCall.status === 'pending' || toolCall.status === 'executing').length;
		if (activeCount > 0) return activeCount === 1 ? 'exploring' : `exploring ${activeCount}`;
		return toolCalls.length === 1 ? 'explored 1' : `explored ${toolCalls.length}`;
	}

	function explorationRows(toolCalls: ToolCall[]) {
		const rows: string[] = [];
		let pendingReads: ToolCall[] = [];

		for (const toolCall of toolCalls) {
			if (normalizeToolName(toolCall.name) === 'READFILE' && !isFailedExploration(toolCall)) {
				pendingReads.push(toolCall);
				continue;
			}

			if (pendingReads.length) {
				rows.push(formatExplorationReadGroup(pendingReads));
				pendingReads = [];
			}
			rows.push(summarizeExplorationTool(toolCall));
		}

		if (pendingReads.length) rows.push(formatExplorationReadGroup(pendingReads));
		return rows.length ? rows : ['Inspecting'];
	}

	function formatExplorationReadGroup(reads: ToolCall[]) {
		const counts = new Map<string, number>();
		for (const read of reads) {
			const args = parseToolArgs(read);
			const summary = parseExplorationSummary(read);
			const label = shortPathLabel(summary.path ?? args.path);
			counts.set(label, (counts.get(label) ?? 0) + 1);
		}

		const parts = [...counts.entries()].map(([label, count]) => count === 1 ? label : `${label} x${count}`);
		let text = `Read ${parts.join(', ')}`;
		const summaries = reads.map(parseExplorationSummary);
		if (summaries.some((summary) => summary.truncated || summary.hasMore)) text += ' truncated';
		if (summaries.some((summary) => summary.unchanged)) text += ' unchanged';
		return text;
	}

	function addExplorationMarkers(text: string, summary: ExplorationSummary) {
		if (summary.truncated || summary.hasMore) return `${text} truncated`;
		return text;
	}

	function isFailedExploration(toolCall: ToolCall) {
		return toolCall.status === 'error' || parseExplorationSummary(toolCall).isError === true;
	}

	function bestExplorationSubject(toolCall: ToolCall, summary = parseExplorationSummary(toolCall)) {
		const args = parseToolArgs(toolCall);
		return String(summary.path ?? args.path ?? args.pattern ?? '');
	}

	function verbForExplorationTool(toolName: string) {
		const normalized = normalizeToolName(toolName);
		if (normalized === 'READFILE') return 'Read';
		if (normalized === 'GREP') return 'Search';
		if (normalized === 'GLOBSEARCH') return 'Find';
		if (normalized === 'LISTDIRECTORY') return 'List';
		return toolName;
	}

	function escapeHtml(value: string) {
		return value
			.replace(/&/g, '&amp;')
			.replace(/</g, '&lt;')
			.replace(/>/g, '&gt;')
			.replace(/"/g, '&quot;')
			.replace(/'/g, '&#039;');
	}

	function renderInlineMarkdown(value: string) {
		return escapeHtml(value)
			.replace(/`([^`]+)`/g, '<code>$1</code>')
			.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
			.replace(/\*([^*]+)\*/g, '<em>$1</em>')
			.replace(/\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g, '<a href="$2" target="_blank" rel="noreferrer">$1</a>');
	}

	function renderMarkdown(source: string) {
		const lines = source.replace(/\r\n/g, '\n').split('\n');
		const parts: string[] = [];
		let paragraph: string[] = [];
		let listItems: string[] = [];
		let inCode = false;
		let codeLines: string[] = [];

		const flushParagraph = () => {
			if (paragraph.length === 0) return;
			parts.push(`<p>${renderInlineMarkdown(paragraph.join(' '))}</p>`);
			paragraph = [];
		};

		const flushList = () => {
			if (listItems.length === 0) return;
			parts.push(`<ul>${listItems.map((item) => `<li>${renderInlineMarkdown(item)}</li>`).join('')}</ul>`);
			listItems = [];
		};

		for (const line of lines) {
			const fence = line.match(/^```/);
			if (fence) {
				if (inCode) {
					parts.push(`<pre><code>${escapeHtml(codeLines.join('\n'))}</code></pre>`);
					codeLines = [];
					inCode = false;
				} else {
					flushParagraph();
					flushList();
					inCode = true;
				}
				continue;
			}

			if (inCode) {
				codeLines.push(line);
				continue;
			}

			if (!line.trim()) {
				flushParagraph();
				flushList();
				continue;
			}

			const heading = line.match(/^(#{1,3})\s+(.+)$/);
			if (heading) {
				flushParagraph();
				flushList();
				const level = heading[1].length + 2;
				parts.push(`<h${level}>${renderInlineMarkdown(heading[2])}</h${level}>`);
				continue;
			}

			const listItem = line.match(/^\s*[-*]\s+(.+)$/);
			if (listItem) {
				flushParagraph();
				listItems.push(listItem[1]);
				continue;
			}

			const quote = line.match(/^>\s?(.+)$/);
			if (quote) {
				flushParagraph();
				flushList();
				parts.push(`<blockquote>${renderInlineMarkdown(quote[1])}</blockquote>`);
				continue;
			}

			flushList();
			paragraph.push(line.trim());
		}

		if (inCode) {
			parts.push(`<pre><code>${escapeHtml(codeLines.join('\n'))}</code></pre>`);
		}
		flushParagraph();
		flushList();

		return parts.join('');
	}

	function registerMessageElement(
		node: HTMLElement,
		details: {
			id: string;
			register: (id: string, el: HTMLElement) => void;
			unregister: (id: string) => void;
		}
	) {
		details.register(details.id, node);

		return {
			destroy() {
				details.unregister(details.id);
			}
		};
	}
</script>

<svelte:head>
	<title>HPD-OS Workspace</title>
</svelte:head>

<div class="app-provider">
	<div class="workspace-shell" class:transcript-collapsed={transcriptCollapsed}>
		<aside class="sidebar" class:collapsed={sidebarCollapsed}>
			<section class="sidebar-section">
				<div class="section-header">
					<h1>Workspaces</h1>
					<div class="icon-row">
						<button class="icon-button" type="button" aria-label="Add workspace" title="Add workspace" onclick={() => openWorkspaceModal('create')}>
							+
						</button>
						<button class="icon-button" type="button" aria-label="Refresh workspaces" title="Refresh workspaces" onclick={() => loadWorkspaces()}>
							R
						</button>
						<button class="icon-button" type="button" aria-label={sidebarCollapsed ? 'Expand sidebar' : 'Collapse sidebar'} title={sidebarCollapsed ? 'Expand sidebar' : 'Collapse sidebar'} onclick={() => (sidebarCollapsed = !sidebarCollapsed)}>
							{sidebarCollapsed ? '>' : '<'}
						</button>
					</div>
				</div>

				{#if workspaceProfiles.length === 0}
					<div class="empty-chip">No workspaces configured.</div>
				{:else}
					<div class="workspace-list">
						{#each workspaceProfiles as profile (profile.key)}
							<div
								class="workspace-card"
								class:active={profile.key === activeWorkspaceKey}
								title={profile.name}
							>
								<button type="button" class="workspace-select" onclick={() => selectWorkspace(profile)}>
									<span class="workspace-icon">{profile.roots && profile.roots.length > 1 ? '[]' : '/'}</span>
									{#if !sidebarCollapsed}
										<span class="workspace-meta">
											<span class="workspace-name">{profile.name}</span>
											<span class="workspace-path">{profile.roots?.[0]?.path ?? 'No root'}</span>
										</span>
									{/if}
								</button>
								{#if !sidebarCollapsed}
									<button class="mini-button" type="button" aria-label="Edit workspace" onclick={() => openWorkspaceModal('edit', profile)}>e</button>
									<button class="mini-button danger" type="button" aria-label="Delete workspace" onclick={() => deleteWorkspace(profile)}>x</button>
								{/if}
							</div>
						{/each}
					</div>
				{/if}
			</section>

			<section class="sidebar-section sessions-section">
				<div class="section-header">
					<h2>{sidebarCollapsed ? 'Chats' : 'Workspace Sessions'}</h2>
					<button class="icon-button" type="button" aria-label="Create new session" title="Create new session" onclick={createSessionForActiveWorkspace}>+</button>
				</div>

				{#if workspace}
					<SessionList.Root
						sessions={workspaceSessions}
						activeSessionId={activeSessionMatchesWorkspace ? workspace.activeSessionId : null}
						onSelect={(id) => workspace?.selectSession(id)}
					>
						{#snippet children(state)}
							{#if state.isEmpty}
								<SessionList.Empty>
									{#snippet children()}
										<div class="empty-chip">No sessions in this workspace.</div>
									{/snippet}
								</SessionList.Empty>
							{:else}
								<ul class="session-list">
									{#each workspaceSessions as session (session.id)}
										<SessionList.Item {session}>
											{#snippet child(item)}
												<li {...item.props} class="session-item" class:active={item.isActive} title={formatSessionLabel(session)}>
													<div class="session-select">
															<span class="session-dot"></span>
															{#if !sidebarCollapsed}
																<span class="session-copy">
																	<span>{formatSessionLabel(session)}</span>
																	<small>{session.id}</small>
																</span>
															{/if}
													</div>
														{#if !sidebarCollapsed}
															<button class="mini-button" type="button" aria-label="Rename session" onclick={(event) => {
																event.stopPropagation();
																renameSession(session);
															}}>e</button>
															<button class="mini-button danger" type="button" aria-label="Delete session" onclick={(event) => {
																event.stopPropagation();
																deleteSession(session.id);
															}}>x</button>
														{/if}
												</li>
											{/snippet}
										</SessionList.Item>
									{/each}
								</ul>
							{/if}
						{/snippet}
					</SessionList.Root>
				{:else}
					<div class="empty-chip">Connecting...</div>
				{/if}
			</section>

			{#if !sidebarCollapsed}
				<section class="sidebar-section all-sessions-section">
					<div class="section-header">
						<h2>All Sessions</h2>
					</div>
					{#if workspace}
						<SessionList.Root
							sessions={allSessions}
							activeSessionId={workspace.activeSessionId}
							onSelect={(id) => {
								const session = allSessions.find((item) => item.id === id);
								if (session) void selectSessionFromAll(session);
							}}
						>
							{#snippet children(state)}
								{#if state.isEmpty}
									<SessionList.Empty>
										{#snippet children()}
											<div class="empty-chip">No sessions available.</div>
										{/snippet}
									</SessionList.Empty>
								{:else}
									<ul class="session-list compact">
										{#each allSessions as session (session.id)}
											<SessionList.Item {session}>
												{#snippet child(item)}
													<li {...item.props} class="session-item" class:active={item.isActive} title={formatSessionLabel(session)}>
														<div class="session-select">
															<span class="session-dot"></span>
															<span class="session-copy">
																<span>{formatSessionLabel(session)}</span>
																<small>{typeof session.metadata?.workspaceKey === 'string' ? session.metadata.workspaceKey : 'unscoped'}</small>
															</span>
														</div>
													</li>
												{/snippet}
											</SessionList.Item>
										{/each}
									</ul>
								{/if}
							{/snippet}
						</SessionList.Root>
					{/if}
				</section>
			{/if}

			<section class="sidebar-footer">
				<button class="settings-button" type="button" onclick={() => (settingsOpen = true)}>
					<span class="settings-icon">#</span>
					{#if !sidebarCollapsed}
						<span>
							<strong>Settings</strong>
							<small>Providers, model, runtime</small>
						</span>
					{/if}
				</button>
			</section>
		</aside>

		{#if !transcriptCollapsed}
			<main class="transcript-panel">
				<div class="messages-scroll">
					{#if loadError}
						<div class="notice error">{loadError}</div>
					{/if}

					{#if !workspace}
						<div class="placeholder">
							<h2>HPD-OS Workspace</h2>
							<p>Loading the Svelte workspace shell.</p>
						</div>
					{:else if !activeWorkspace && !activeSessionMatchesWorkspace}
						<div class="placeholder">
							<h2>Choose a workspace profile.</h2>
							<p>Create or select a workspace to scope sessions and coding roots.</p>
						</div>
					{:else if messages.length === 0}
						<div class="placeholder">
							<h2>{activeWorkspace?.name ?? 'Selected session'}</h2>
							<p>{activeWorkspace ? activeRootSummary : 'No workspace metadata is attached to this session.'}</p>
						</div>
					{:else}
							<MessageList.Root {messages} scrollBehavior="sent-message" class="message-list-root">
								{#snippet children(listState)}
									{#each messages as message, index (message.id)}
										<Message {message}>
											{#snippet child(messageState)}
												<MessageEdit.Root
													workspace={workspace as Workspace}
													messageIndex={index}
													initialValue={message.content}
													editing={editingIndex === index}
													onStartEdit={() => (editingIndex = index)}
													onSave={() => (editingIndex = null)}
													onCancel={() => (editingIndex = null)}
												>
													{#snippet children(editState)}
														<article
															{...messageState.props}
															class="message-card"
															use:registerMessageElement={{
																id: message.id,
																register: listState.registerMessageElement,
																unregister: listState.unregisterMessageElement
															}}
														>
															<header class="message-header">
																<span>{message.role === 'user' ? 'You' : 'Assistant'}</span>
																<time>{formatTime(message)}</time>
															</header>

															{#if message.reasoning}
																<details class="reasoning-panel">
																	<summary>Reasoning</summary>
																	<p>{message.reasoning}</p>
																</details>
															{/if}

															{#if message.toolCalls?.length}
																{@const explorationCalls = explorationToolCalls(message.toolCalls)}
																{@const regularCalls = regularToolCalls(message.toolCalls)}
																{@const explorationDisplayRows = explorationRows(explorationCalls)}
																<div class="tool-stack">
																	{#if explorationCalls.length}
																		<details class="exploration-card">
																			<summary>
																				<span class="tool-dot"></span>
																				<span>Exploration</span>
																				<small>{summarizeExplorationGroup(explorationCalls)}</small>
																			</summary>
																			<div class="exploration-body">
																				<div class="exploration-rows">
																					{#each explorationDisplayRows as row}
																						<div class="exploration-row">{row}</div>
																					{/each}
																				</div>
																				{#each explorationCalls as toolCall (toolCall.callId)}
																					<details class="exploration-op">
																						<summary>
																							<span>{summarizeExplorationTool(toolCall)}</span>
																							<small>{toolCall.status}</small>
																						</summary>
																						<div class="exploration-op-body">
																							{#if Object.keys(toolCall.args ?? {}).length}
																								<div>
																									<div class="tool-label">Arguments</div>
																									<pre>{JSON.stringify(toolCall.args, null, 2)}</pre>
																								</div>
																							{/if}
																							{#if formatToolResult(toolCall)}
																								<div>
																									<div class="tool-label">Result output</div>
																									<pre>{formatToolResult(toolCall)}</pre>
																								</div>
																							{/if}
																						</div>
																					</details>
																				{/each}
																			</div>
																		</details>
																	{/if}
																	{#each regularCalls as toolCall (toolCall.callId)}
																		<ToolExecution.Root {toolCall} class="tool-card">
																			{#snippet children(toolState)}
																				<ToolExecution.Trigger class="tool-trigger">
																					{#snippet children(triggerState)}
																						<span class="tool-dot"></span>
																						<span>{toolState.name}</span>
																						<small>{toolState.status}</small>
																						<span>{triggerState.expanded ? '-' : '+'}</span>
																					{/snippet}
																				</ToolExecution.Trigger>
																				{#if toolState.expanded}
																					<ToolExecution.Content class="tool-body">
																						{#snippet children()}
																							<ToolExecution.Args>
																								{#snippet children(argsState)}
																									{#if argsState.hasArgs}
																										<pre>{argsState.argsJson}</pre>
																									{/if}
																								{/snippet}
																							</ToolExecution.Args>
																							<ToolExecution.Result>
																								{#snippet children(resultState)}
																									{#if resultState.hasError && resultState.error}
																										<pre>{resultState.error}</pre>
																									{:else if resultState.hasResult && resultState.result}
																										<pre>{resultState.result}</pre>
																									{:else if formatToolResult(toolCall)}
																										<pre>{formatToolResult(toolCall)}</pre>
																									{/if}
																								{/snippet}
																							</ToolExecution.Result>
																						{/snippet}
																					</ToolExecution.Content>
																				{/if}
																			{/snippet}
																		</ToolExecution.Root>
																	{/each}
																</div>
															{/if}

															{#if editState.editing && message.role === 'user'}
																<div class="edit-panel">
																	<MessageEdit.Textarea class="edit-textarea" />
																	<div class="edit-actions">
																		<MessageEdit.CancelButton class="action-button">Cancel</MessageEdit.CancelButton>
																		<MessageEdit.SaveButton class="action-button primary">Save & Send</MessageEdit.SaveButton>
																	</div>
																</div>
															{:else}
																<div class="message-content markdown-output">
																	{@html message.role === 'assistant' ? renderMarkdown(message.content) : escapeHtml(message.content).replace(/\n/g, '<br>')}{#if message.streaming}<span class="cursor">|</span>{/if}
																</div>
															{/if}
														</article>

														{#if workspace && !editState.editing}
															<MessageActions.Root {workspace} messageIndex={index} role={message.role} branch={activeBranch}>
																{#snippet children(actions)}
																	<div class="message-actions" data-role={message.role}>
																		{#if message.role === 'user'}
																			<button type="button" class="action-button" onclick={editState.startEdit}>Edit</button>
																		{/if}
																		<MessageActions.RetryButton>
																			{#snippet child(retry)}
																				<button {...retry.props} type="button" class="action-button" onclick={retry.retry}>Retry</button>
																			{/snippet}
																		</MessageActions.RetryButton>
																		<MessageActions.CopyButton content={message.content}>
																			{#snippet child(copy)}
																				<button {...copy.props} type="button" class="action-button" onclick={copy.copy}>{copy.copied ? 'Copied' : 'Copy'}</button>
																			{/snippet}
																		</MessageActions.CopyButton>
																		{#if message.role === 'user' && actions.hasSiblings}
																			<MessageActions.Prev class="action-button">Prev</MessageActions.Prev>
																			<MessageActions.Position class="position-pill" />
																			<MessageActions.Next class="action-button">Next</MessageActions.Next>
																		{/if}
																	</div>
																{/snippet}
															</MessageActions.Root>
														{/if}
													{/snippet}
												</MessageEdit.Root>
											{/snippet}
										</Message>
									{/each}
								{/snippet}
							</MessageList.Root>
					{/if}
				</div>
			</main>
		{/if}

		<aside class="workspace-rail">
			<div class="workspace-surface-shell">
				<div class="workspace-surface">
					<header>
						<div>
							<h2>HPD-OS</h2>
							<p>{activeWorkspace ? activeWorkspace.name : 'No workspace selected'}</p>
						</div>
						<span class="surface-status" data-active={isStreaming || activeTools.length > 0}>
							{monitorStatus}
						</span>
					</header>

					<div class="surface-grid">
						<section>
							<span class="surface-label">Workspace Root</span>
							<code>{activeRootSummary}</code>
						</section>
						<section>
							<span class="surface-label">Session</span>
							<code>{activeSession ? formatSessionLabel(activeSession) : 'No active session'}</code>
						</section>
						<section>
							<span class="surface-label">Branch</span>
							<code>{workspace?.activeBranchId ?? 'No branch'}</code>
						</section>
						<section>
							<span class="surface-label">Model</span>
							<code>{runConfig.modelId ?? 'Default model'}</code>
						</section>
					</div>

					<div class="surface-activity">
						<div class="surface-label">Current Activity</div>
						{#if monitorItems.length}
							<div class="surface-activity-list">
								{#each monitorItems as item (item.id)}
									<div class="surface-activity-row" data-status={item.status}>
										<span class="monitor-dot"></span>
										<span>
											<strong>{item.label}</strong>
											<small>{item.detail}</small>
										</span>
									</div>
								{/each}
							</div>
						{:else}
							<p>No activity yet.</p>
						{/if}
					</div>
				</div>
			</div>

			<footer class="composer-area">
				<div class="composer-card">
						<ChatInput.Root
							value={composerValue}
							disabled={!canSend}
							onChange={(value) => (composerValue = value)}
							onSubmit={(details) => {
								composerValue = '';
								void sendMessage(details.value);
							}}
						>
						{#snippet children()}
							<ChatInput.Input placeholder="Type an instruction for the agent..." minRows={2} maxRows={6} class="composer-input" />
							<div class="composer-bottom">
								<ChatInput.Bottom>
									{#snippet children(inputState)}
										<div class="model-strip">
											<span>{runConfig.modelId ?? 'Default model'}</span>
											{#if isStreaming}
												<button type="button" class="stop-button" onclick={() => workspace?.abort()}>Stop</button>
											{/if}
										</div>
										<button class="send-button" type="button" disabled={!inputState.canSubmit} onclick={inputState.submit}>Send</button>
									{/snippet}
								</ChatInput.Bottom>
							</div>
						{/snippet}
					</ChatInput.Root>
				</div>

				<div class="monitor-card">
					<button class="icon-button monitor-toggle" type="button" aria-label={transcriptCollapsed ? 'Expand transcript' : 'Collapse transcript'} onclick={() => (transcriptCollapsed = !transcriptCollapsed)}>
						{transcriptCollapsed ? '>' : '<'}
					</button>
					<div class="monitor-content">
						<header class="monitor-header">
							<strong>{monitorStatus}</strong>
							<span>{messages.length} messages</span>
						</header>
						<div class="monitor-feed" aria-live="polite">
							{#each monitorItems as item (item.id)}
								<div class="monitor-row" data-status={item.status}>
									<span class="monitor-dot"></span>
									<span class="monitor-row-copy">
										<span>{item.label}</span>
										<small>{item.detail}</small>
									</span>
								</div>
							{/each}
						</div>
					</div>
				</div>
			</footer>
		</aside>
	</div>

	{#if workspaceModalOpen}
		<div class="modal-backdrop">
			<section class="settings-modal workspace-modal">
				<header class="modal-header">
					<div>
						<h2>{workspaceFormMode === 'edit' ? 'Edit Workspace Profile' : 'Create Workspace Profile'}</h2>
						<p>Define one or more absolute local directories for this coding workspace.</p>
					</div>
					<button class="icon-button" type="button" onclick={closeWorkspaceModal}>x</button>
				</header>

				<div class="settings-grid">
					<label class="settings-field wide">
						<span class="field-label">Workspace Name</span>
						<input value={workspaceFormName} placeholder="Client project, monorepo, research spike..." oninput={(event) => (workspaceFormName = event.currentTarget.value)} />
					</label>

					<div class="settings-field wide">
						<div class="section-header">
							<span class="field-label">Workspace Directories</span>
							<button type="button" onclick={addWorkspaceRoot}>Add Directory</button>
						</div>

						<div class="workspace-root-editor">
							{#each workspaceFormRoots as root, index}
								<div class="workspace-root-row">
									<label class="settings-field">
										<span class="field-label">Label</span>
										<input value={root.label ?? ''} placeholder="frontend, api, docs" oninput={(event) => updateWorkspaceRoot(index, { label: event.currentTarget.value })} />
									</label>
									<label class="settings-field">
										<span class="field-label">Absolute Path</span>
										<input value={root.path} placeholder="/Users/name/code/project" oninput={(event) => updateWorkspaceRoot(index, { path: event.currentTarget.value })} />
									</label>
									<div class="root-actions">
										<button type="button" onclick={() => browseWorkspaceRoot(index)}>Browse</button>
										<button class="danger" type="button" onclick={() => removeWorkspaceRoot(index)}>Remove</button>
									</div>
								</div>
							{/each}
						</div>
					</div>
				</div>

				<footer class="modal-actions">
					<button type="button" onclick={closeWorkspaceModal}>Cancel</button>
					<button type="button" class="primary" onclick={saveWorkspaceForm}>Save Workspace</button>
				</footer>
			</section>
		</div>
	{/if}

	{#if settingsOpen}
		<div class="modal-backdrop">
			<section class="settings-modal">
				<header class="modal-header">
					<div>
						<h2>Workspace Execution Parameters</h2>
						<p>Configure provider, model, credentials, and run behavior.</p>
					</div>
					<button class="icon-button" type="button" onclick={() => (settingsOpen = false)}>x</button>
				</header>

				<div class="settings-grid">
					<div class="settings-field wide">
						<span class="field-label">Model</span>
						<RunConfig.ModelSelector {runConfig} providers={modelProviders}>
							{#snippet children(modelSelector)}
								<select
									onchange={(event) => {
										const [providerKey, modelId] = event.currentTarget.value.split('::');
										modelSelector.setModel(providerKey || undefined, modelId || undefined);
									}}
								>
									<option value="">Default</option>
									{#each modelSelector.providers as provider}
										<optgroup label={provider.label}>
											{#each provider.models as model}
												<option value="{provider.key}::{model.id}" selected={modelSelector.providerKey === provider.key && modelSelector.modelId === model.id}>{model.label}</option>
											{/each}
										</optgroup>
									{/each}
								</select>
							{/snippet}
						</RunConfig.ModelSelector>
					</div>

					<div class="settings-field">
						<span class="field-label">Temperature <span>{runConfig.temperature?.toFixed(2) ?? 'default'}</span></span>
						<RunConfig.TemperatureSlider {runConfig}>
							{#snippet children(slider)}
								<input type="range" min={slider.min} max={slider.max} step={slider.step} value={slider.value ?? 0.7} oninput={(event) => slider.setValue(Number(event.currentTarget.value))} />
							{/snippet}
						</RunConfig.TemperatureSlider>
					</div>

					<div class="settings-field">
						<span class="field-label">Top P <span>{runConfig.topP?.toFixed(2) ?? 'default'}</span></span>
						<RunConfig.TopPSlider {runConfig}>
							{#snippet children(slider)}
								<input type="range" min={slider.min} max={slider.max} step={slider.step} value={slider.value ?? 1} oninput={(event) => slider.setValue(Number(event.currentTarget.value))} />
							{/snippet}
						</RunConfig.TopPSlider>
					</div>

					<div class="settings-field">
						<span class="field-label">Max Tokens</span>
						<RunConfig.MaxTokensInput {runConfig}>
							{#snippet children(tokens)}
								<input type="number" min={tokens.min} value={tokens.value ?? ''} placeholder="default" oninput={(event) => {
									const value = Number.parseInt(event.currentTarget.value, 10);
									tokens.setValue(Number.isFinite(value) ? value : undefined);
								}} />
							{/snippet}
						</RunConfig.MaxTokensInput>
					</div>

					<label class="settings-field">
						<span class="field-label">Top K</span>
						<input type="number" value={runConfig.topK ?? ''} placeholder="default" oninput={(event) => {
							const value = Number.parseInt(event.currentTarget.value, 10);
							runConfig.setTopK(Number.isFinite(value) ? value : undefined);
						}} />
					</label>

					<label class="settings-field">
						<span class="field-label">Frequency Penalty</span>
						<input type="number" step="0.1" value={runConfig.frequencyPenalty ?? ''} placeholder="-2.0 to 2.0" oninput={(event) => {
							const value = Number.parseFloat(event.currentTarget.value);
							runConfig.setFrequencyPenalty(Number.isFinite(value) ? value : undefined);
						}} />
					</label>

					<label class="settings-field">
						<span class="field-label">Presence Penalty</span>
						<input type="number" step="0.1" value={runConfig.presencePenalty ?? ''} placeholder="-2.0 to 2.0" oninput={(event) => {
							const value = Number.parseFloat(event.currentTarget.value);
							runConfig.setPresencePenalty(Number.isFinite(value) ? value : undefined);
						}} />
					</label>

					<label class="settings-field">
						<span class="field-label">Chat Model ID</span>
						<input value={runConfig.chatModelId ?? ''} placeholder="ChatOptions model override" oninput={(event) => runConfig.setChatModelId(event.currentTarget.value.trim() || undefined)} />
					</label>

					<label class="settings-field wide">
						<span class="field-label">Stop Sequences</span>
						<textarea value={stopSequencesText} placeholder="One stop sequence per line" oninput={(event) => {
							stopSequencesText = event.currentTarget.value;
							runConfig.setStopSequences(parseStopSequences(stopSequencesText) ?? undefined);
						}}></textarea>
					</label>

					<div class="settings-field wide">
						<span class="field-label">System Instructions</span>
						<textarea value={runConfig.systemInstructions ?? ''} placeholder="Completely replace configured instructions for this run..." oninput={(event) => runConfig.setSystemInstructions(event.currentTarget.value.trim() || undefined)}></textarea>
					</div>

					<div class="settings-field wide">
						<span class="field-label">Additional Instructions</span>
						<RunConfig.SystemInstructionsInput {runConfig}>
							{#snippet children(instructions)}
								<textarea value={instructions.value ?? ''} placeholder="Specific platform guidelines appended to runs..." oninput={(event) => instructions.setValue(event.currentTarget.value.trim() || undefined)}></textarea>
							{/snippet}
						</RunConfig.SystemInstructionsInput>
					</div>

					<label class="settings-field">
						<span class="field-label">Custom API Key</span>
						<input type="password" value={providerApiKey} placeholder="Inherited system environment key" oninput={(event) => {
							providerApiKey = event.currentTarget.value;
							runConfig.setApiKey(providerApiKey.trim() || undefined);
						}} />
					</label>

					<label class="settings-field">
						<span class="field-label">Provider Endpoint</span>
						<input value={providerEndpoint} placeholder="Self-hosted, local, Azure, or proxy URL" oninput={(event) => {
							providerEndpoint = event.currentTarget.value;
							runConfig.setProviderEndpoint(providerEndpoint.trim() || undefined);
						}} />
					</label>

					<label class="settings-field">
						<span class="field-label">Model Transport</span>
						<select value={runConfig.modelTransport?.toString() ?? ''} onchange={(event) => runConfig.setModelTransport(event.currentTarget.value ? Number(event.currentTarget.value) as AgentModelTransportMode : undefined)}>
							<option value="">Default</option>
							<option value="0">Auto</option>
							<option value="1">Chat</option>
							<option value="2">Realtime</option>
						</select>
					</label>

					<label class="settings-field">
						<span class="field-label">Upload Strategy</span>
						<select value={runConfig.uploadStrategy?.toString() ?? ''} onchange={(event) => runConfig.setUploadStrategy(event.currentTarget.value ? Number(event.currentTarget.value) as UploadStrategy : undefined)}>
							<option value="">Default</option>
							<option value="0">Auto</option>
							<option value="1">Hosted</option>
							<option value="2">Local</option>
						</select>
					</label>

					<label class="settings-field">
						<span class="field-label">Run Timeout</span>
						<input value={runConfig.runTimeout ?? ''} placeholder="PT5M" oninput={(event) => runConfig.setRunTimeout(event.currentTarget.value.trim() || undefined)} />
					</label>

					<label class="settings-field">
						<span class="field-label">Conversation Override</span>
						<input value={runConfig.conversationIdOverride ?? ''} placeholder="conversation id" oninput={(event) => runConfig.setConversationIdOverride(event.currentTarget.value.trim() || undefined)} />
					</label>

					<label class="settings-field wide">
						<span class="field-label">Provider Options JSON</span>
						<textarea value={providerOptionsText} placeholder={providerOptionsPlaceholder} oninput={(event) => {
							providerOptionsText = event.currentTarget.value;
							setJsonObjectField(providerOptionsText, (nextValue) => runConfig.setProviderOptions(nextValue));
						}}></textarea>
					</label>

					<label class="settings-field wide">
						<span class="field-label">Custom Headers JSON</span>
						<textarea value={customHeadersText} placeholder={customHeadersPlaceholder} oninput={(event) => {
							customHeadersText = event.currentTarget.value;
							setStringRecordField(customHeadersText, (nextValue) => runConfig.setCustomHeaders(nextValue));
						}}></textarea>
					</label>

					<label class="settings-field wide">
						<span class="field-label">Clients JSON</span>
						<textarea value={clientsText} placeholder={clientsPlaceholder} oninput={(event) => {
							clientsText = event.currentTarget.value;
							setJsonObjectField(clientsText, (nextValue) => runConfig.setClients(nextValue));
						}}></textarea>
					</label>

					<label class="settings-field wide">
						<span class="field-label">Context Overrides JSON</span>
						<textarea value={contextOverridesText} placeholder={jsonObjectPlaceholder} oninput={(event) => {
							contextOverridesText = event.currentTarget.value;
							setJsonObjectField(contextOverridesText, (nextValue) => runConfig.setContextOverrides(nextValue));
						}}></textarea>
					</label>

					<label class="settings-field wide">
						<span class="field-label">Chat Additional Properties JSON</span>
						<textarea value={chatAdditionalPropertiesText} placeholder={jsonObjectPlaceholder} oninput={(event) => {
							chatAdditionalPropertiesText = event.currentTarget.value;
							setJsonObjectField(chatAdditionalPropertiesText, (nextValue) => runConfig.setChatAdditionalProperties(nextValue));
						}}></textarea>
					</label>

					<label class="settings-field wide">
						<span class="field-label">Reasoning JSON</span>
						<textarea value={reasoningText} placeholder={reasoningPlaceholder} oninput={(event) => {
							reasoningText = event.currentTarget.value;
							setJsonObjectField(reasoningText, (nextValue) => runConfig.setReasoning(nextValue));
						}}></textarea>
					</label>

					<label class="settings-field wide">
						<span class="field-label">Client Tool Input JSON</span>
						<textarea value={clientToolInputText} placeholder={jsonObjectPlaceholder} oninput={(event) => {
							clientToolInputText = event.currentTarget.value;
							setJsonObjectField(clientToolInputText, (nextValue) => runConfig.setClientToolInput(nextValue));
						}}></textarea>
					</label>

					<label class="settings-field wide">
						<span class="field-label">Audio JSON</span>
						<textarea value={audioText} placeholder={audioPlaceholder} oninput={(event) => {
							audioText = event.currentTarget.value;
							setJsonObjectField(audioText, (nextValue) => runConfig.setAudio(nextValue));
						}}></textarea>
					</label>

					<label class="settings-field wide">
						<span class="field-label">Structured Output JSON</span>
						<textarea value={structuredOutputText} placeholder={structuredOutputPlaceholder} oninput={(event) => {
							structuredOutputText = event.currentTarget.value;
							setJsonObjectField(structuredOutputText, (nextValue) => runConfig.setStructuredOutput(nextValue));
						}}></textarea>
					</label>

					<div class="settings-field wide">
						<span class="field-label">Boolean Overrides</span>
						<div class="permission-grid">
							<label class="permission-row"><code>useCache</code><input type="checkbox" checked={runConfig.useCache === true} oninput={(event) => runConfig.setUseCache(event.currentTarget.checked ? true : undefined)} /></label>
							<label class="permission-row"><code>coalesceDeltas</code><input type="checkbox" checked={runConfig.coalesceDeltas === true} oninput={(event) => runConfig.setCoalesceDeltas(event.currentTarget.checked ? true : undefined)} /></label>
							<label class="permission-row"><code>skipTools</code><input type="checkbox" checked={runConfig.skipTools === true} oninput={(event) => runConfig.setSkipTools(event.currentTarget.checked ? true : undefined)} /></label>
							<label class="permission-row"><code>allowBackgroundResponses</code><input type="checkbox" checked={runConfig.allowBackgroundResponses === true} oninput={(event) => runConfig.setAllowBackgroundResponses(event.currentTarget.checked ? true : undefined)} /></label>
							<label class="permission-row"><code>triggerCompaction</code><input type="checkbox" checked={runConfig.triggerCompaction === true} oninput={(event) => runConfig.setTriggerCompaction(event.currentTarget.checked ? true : undefined)} /></label>
							<label class="permission-row"><code>skipCompaction</code><input type="checkbox" checked={runConfig.skipCompaction === true} oninput={(event) => runConfig.setSkipCompaction(event.currentTarget.checked ? true : undefined)} /></label>
						</div>
					</div>

					<label class="settings-field">
						<span class="field-label">Background Polling Interval</span>
						<input value={runConfig.backgroundPollingInterval ?? ''} placeholder="PT2S" oninput={(event) => runConfig.setBackgroundPollingInterval(event.currentTarget.value.trim() || undefined)} />
					</label>

					<label class="settings-field">
						<span class="field-label">Background Timeout</span>
						<input value={runConfig.backgroundTimeout ?? ''} placeholder="PT5M" oninput={(event) => runConfig.setBackgroundTimeout(event.currentTarget.value.trim() || undefined)} />
					</label>

					<label class="settings-field">
						<span class="field-label">Compaction Behavior</span>
						<select value={runConfig.compactionBehaviorOverride?.toString() ?? ''} onchange={(event) => runConfig.setCompactionBehaviorOverride(event.currentTarget.value ? Number(event.currentTarget.value) as CompactionBehavior : undefined)}>
							<option value="">Default</option>
							<option value="0">Continue</option>
							<option value="1">Circuit Breaker</option>
						</select>
					</label>

					<div class="settings-field wide">
						<span class="field-label">Permission Overrides</span>
						<RunConfig.PermissionOverridesPanel {runConfig} permissions={permissionKeys}>
							{#snippet children(permissions)}
								<div class="permission-grid">
									{#each permissions.items as item}
										<div class="permission-row">
											<code>{item.key}</code>
											<div>
												<button class:active={item.value === true} type="button" onclick={() => permissions.setOverride(item.key, true)}>Allow</button>
												<button class:active={item.value === false} type="button" onclick={() => permissions.setOverride(item.key, false)}>Deny</button>
												<button class:active={item.value === undefined} type="button" onclick={() => permissions.setOverride(item.key, undefined)}>Default</button>
											</div>
										</div>
									{/each}
								</div>
							{/snippet}
						</RunConfig.PermissionOverridesPanel>
					</div>
				</div>

				<footer class="modal-actions">
					<button type="button" onclick={() => {
						runConfig.reset();
						providerApiKey = '';
						providerEndpoint = '';
						providerOptionsText = '';
						customHeadersText = '';
						clientsText = '';
						contextOverridesText = '';
						chatAdditionalPropertiesText = '';
						reasoningText = '';
						stopSequencesText = '';
						clientToolInputText = '';
						audioText = '';
						structuredOutputText = '';
					}}>Reset Defaults</button>
					<button type="button" class="primary" onclick={() => (settingsOpen = false)}>Apply Configuration</button>
				</footer>
			</section>
		</div>
	{/if}

	{#if workspace}
		<PermissionDialog.Root agent={workspace}>
			<PermissionDialog.Overlay class="modal-backdrop permission-layer" />
			<PermissionDialog.Content>
				{#snippet child(dialog)}
					{#if dialog.isOpen && dialog.request}
						<section {...dialog.props} class="permission-modal permission-content-layer">
							<PermissionDialog.Header>
								{#snippet child(header)}
									<h2 {...header.props}>{header.functionName ?? 'Permission Requested'}</h2>
								{/snippet}
							</PermissionDialog.Header>
							<PermissionDialog.Description>
								{#snippet child(description)}
									<div {...description.props}>
										<p>{description.description ?? 'The agent requested permission to continue.'}</p>
										<pre>{JSON.stringify(description.arguments ?? {}, null, 2)}</pre>
									</div>
								{/snippet}
							</PermissionDialog.Description>
							<PermissionDialog.Actions class="modal-actions">
								<PermissionDialog.Deny>Decline</PermissionDialog.Deny>
								<PermissionDialog.Approve choice="ask" class="primary">Approve Once</PermissionDialog.Approve>
								<PermissionDialog.Approve choice="allow_always" class="primary subtle">Always Allow</PermissionDialog.Approve>
							</PermissionDialog.Actions>
						</section>
					{/if}
				{/snippet}
			</PermissionDialog.Content>
		</PermissionDialog.Root>
	{/if}
</div>

<style>
	:global(html),
	:global(body) {
		margin: 0;
		height: 100%;
		background: #070708;
		color: #f4f4f5;
		font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
	}

	:global(button),
	:global(input),
	:global(select),
	:global(textarea) {
		font: inherit;
	}

	:global(*),
	:global(*::before),
	:global(*::after) {
		box-sizing: border-box;
	}

	.workspace-shell {
		--bg: #070708;
		--panel: #0c0c0f;
		--input: #121217;
		--border: #1e1e24;
		--muted: #a1a1aa;
		--dim: #52525b;
		--gold: #e0a96d;
		--green: #34d399;
		--red: #ef4444;
		display: flex;
		height: 100vh;
		min-height: 0;
		overflow: hidden;
		background: var(--bg);
		color: #f4f4f5;
	}

	.sidebar {
		width: 16rem;
		flex: 0 0 auto;
		display: flex;
		flex-direction: column;
		border-right: 1px solid var(--border);
		background: var(--bg);
		transition: width 180ms cubic-bezier(0.16, 1, 0.3, 1);
	}

	.sidebar.collapsed {
		width: 4.5rem;
	}

	.sidebar-section {
		border-bottom: 1px solid var(--border);
		padding: 0.85rem;
	}

	.sessions-section {
		flex: 1;
		min-height: 0;
		overflow: auto;
	}

	.all-sessions-section {
		max-height: 38%;
		overflow: auto;
	}

	.sidebar-footer {
		margin-top: auto;
		border-top: 1px solid var(--border);
		padding: 0.75rem;
		background: var(--panel);
	}

	.section-header,
	.modal-header,
	.workspace-surface header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 0.75rem;
	}

	h1,
	h2,
	p {
		margin: 0;
	}

	h1,
	h2 {
		font-size: 0.7rem;
		letter-spacing: 0.16em;
		text-transform: uppercase;
		color: var(--muted);
	}

	.icon-row,
	.message-actions,
	.modal-actions {
		display: flex;
		align-items: center;
		gap: 0.45rem;
	}

	button {
		border: 1px solid var(--border);
		background: #09090b;
		color: var(--muted);
		border-radius: 6px;
		cursor: pointer;
		transition: border-color 150ms, color 150ms, background 150ms;
	}

	button:hover:not(:disabled) {
		border-color: #3f3f46;
		color: white;
	}

	button:disabled {
		cursor: not-allowed;
		opacity: 0.45;
	}

	.icon-button {
		width: 2rem;
		height: 2rem;
		display: inline-flex;
		align-items: center;
		justify-content: center;
	}

	.workspace-list,
	.session-list {
		display: grid;
		gap: 0.45rem;
		margin: 0.75rem 0 0;
		padding: 0;
		list-style: none;
	}

	.workspace-card,
	.workspace-select,
	.session-item,
	.session-select,
	.settings-button {
		width: 100%;
		display: flex;
		align-items: center;
		gap: 0.65rem;
		padding: 0.65rem;
		text-align: left;
		background: rgba(12, 12, 15, 0.65);
	}

	.workspace-card {
		padding: 0;
	}

	.workspace-select {
		min-width: 0;
		flex: 1;
		border: 0;
		background: transparent;
	}

	.session-select {
		min-width: 0;
		flex: 1;
		border: 0;
		padding: 0;
		background: transparent;
	}

	.workspace-card.active,
	.session-item.active {
		border-color: rgba(224, 169, 109, 0.42);
		background: #09090b;
		color: white;
	}

	.workspace-icon,
	.settings-icon,
	.session-dot {
		flex: 0 0 auto;
		display: inline-flex;
		align-items: center;
		justify-content: center;
		width: 1.7rem;
		height: 1.7rem;
		border: 1px solid rgba(224, 169, 109, 0.24);
		border-radius: 7px;
		color: var(--gold);
		background: rgba(224, 169, 109, 0.08);
		font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
		font-size: 0.68rem;
	}

	.session-dot {
		width: 0.55rem;
		height: 0.55rem;
		border-radius: 999px;
		background: var(--dim);
		border: 0;
	}

	.session-item.active .session-dot {
		background: var(--gold);
	}

	.workspace-meta,
	.session-copy,
	.settings-button span:last-child {
		min-width: 0;
		display: grid;
		gap: 0.12rem;
	}

	.workspace-name,
	.session-copy span,
	.settings-button strong {
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
		font-size: 0.76rem;
		color: #f4f4f5;
	}

	.workspace-path,
	.session-copy small,
	.settings-button small,
	.empty-chip,
	.notice {
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
		font-size: 0.68rem;
		color: var(--dim);
	}

	.mini-button {
		margin-left: auto;
		width: 1.55rem;
		height: 1.55rem;
		padding: 0;
	}

	.danger:hover {
		border-color: rgba(239, 68, 68, 0.42);
		color: var(--red);
	}

	.compact {
		gap: 0.3rem;
	}

	.compact .session-item {
		padding: 0.5rem;
	}

	.transcript-panel {
		flex: 0.85 1 0;
		min-width: 25%;
		min-height: 0;
		background: var(--bg);
	}

	.messages-scroll {
		height: 100%;
		overflow: auto;
		padding: 1.5rem;
	}

	:global(.message-list-root) {
		display: grid;
		gap: 1rem;
	}

	.placeholder {
		max-width: 70ch;
		margin: 0 auto;
		padding: 5rem 1rem;
		text-align: center;
		display: grid;
		gap: 0.75rem;
	}

	.placeholder h2 {
		font-size: 0.9rem;
		color: white;
		letter-spacing: 0;
		text-transform: none;
	}

	.placeholder p {
		color: var(--muted);
		font-size: 0.82rem;
	}

	.message-card {
		max-width: 72ch;
		display: grid;
		gap: 0.7rem;
		font-size: 0.82rem;
		line-height: 1.65;
	}

	.message-card[data-role='user'] {
		margin-left: auto;
		max-width: min(72ch, 82%);
		padding: 0.8rem;
		border: 1px solid var(--border);
		border-radius: 8px;
		background: var(--panel);
	}

	.message-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 1rem;
		color: var(--dim);
		font-size: 0.68rem;
	}

	.message-content {
		white-space: pre-wrap;
		overflow-wrap: anywhere;
	}

	.markdown-output {
		white-space: normal;
		color: #e4e4e7;
		line-height: 1.65;
		overflow-wrap: anywhere;
	}

	.markdown-output :global(* + *) {
		margin-top: 0.85rem;
	}

	.markdown-output :global(p),
	.markdown-output :global(h3),
	.markdown-output :global(h4),
	.markdown-output :global(h5) {
		margin-bottom: 0;
	}

	.markdown-output :global(h3),
	.markdown-output :global(h4),
	.markdown-output :global(h5) {
		color: #f4f4f5;
		letter-spacing: 0;
		text-transform: none;
	}

	.markdown-output :global(ul) {
		padding-left: 1.2rem;
	}

	.markdown-output :global(li + li) {
		margin-top: 0.2rem;
	}

	.markdown-output :global(a) {
		color: var(--gold);
		text-decoration: underline;
		text-underline-offset: 2px;
	}

	.markdown-output :global(code) {
		font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
		font-size: 0.92em;
		background: rgba(18, 18, 23, 0.95);
		border: 1px solid rgba(30, 30, 36, 0.9);
		border-radius: 4px;
		padding: 0.1rem 0.35rem;
	}

	.markdown-output :global(pre) {
		background: rgba(10, 10, 13, 0.92);
		border: 1px solid rgba(30, 30, 36, 0.9);
		border-radius: 8px;
		padding: 0.9rem 1rem;
		overflow-x: auto;
	}

	.markdown-output :global(pre code) {
		background: transparent;
		border: 0;
		padding: 0;
		font-size: 0.95em;
		display: block;
		white-space: pre;
	}

	.markdown-output :global(blockquote) {
		border-left: 2px solid rgba(224, 169, 109, 0.55);
		padding-left: 0.9rem;
		color: var(--muted);
	}

	.edit-panel {
		display: grid;
		gap: 0.6rem;
	}

	.edit-textarea {
		min-height: 5rem;
	}

	.edit-actions {
		display: flex;
		justify-content: flex-end;
		gap: 0.45rem;
	}

	.reasoning-panel,
	.tool-card,
	.exploration-card {
		border: 1px solid rgba(30, 30, 36, 0.75);
		border-radius: 7px;
		background: rgba(12, 12, 15, 0.56);
	}

	.reasoning-panel summary,
	.tool-trigger,
	.exploration-card > summary {
		width: 100%;
		padding: 0.6rem 0.7rem;
		color: var(--muted);
		font-size: 0.72rem;
	}

	.exploration-card > summary {
		display: grid;
		grid-template-columns: auto auto 1fr;
		align-items: center;
		gap: 0.55rem;
		cursor: pointer;
		list-style: none;
	}

	.exploration-card > summary small {
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
		color: var(--dim);
	}

	.reasoning-panel p,
	.tool-body,
	.exploration-body {
		padding: 0.7rem;
		border-top: 1px solid var(--border);
		color: var(--muted);
	}

	.exploration-body {
		display: grid;
		gap: 0.5rem;
	}

	.exploration-rows {
		display: grid;
		gap: 0.35rem;
	}

	.exploration-row {
		border: 1px solid rgba(30, 30, 36, 0.62);
		border-radius: 6px;
		background: rgba(7, 7, 8, 0.28);
		padding: 0.45rem 0.55rem;
		color: #d4d4d8;
		font-size: 0.7rem;
		line-height: 1.4;
		overflow-wrap: anywhere;
	}

	.exploration-op {
		border-left: 1px solid rgba(30, 30, 36, 0.9);
		padding-left: 0.7rem;
	}

	.exploration-op > summary {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 0.8rem;
		cursor: pointer;
		list-style: none;
		font-size: 0.7rem;
		color: #d4d4d8;
	}

	.exploration-op > summary small,
	.tool-label {
		color: var(--dim);
		font-size: 0.62rem;
		text-transform: uppercase;
		letter-spacing: 0.12em;
	}

	.exploration-op-body {
		display: grid;
		gap: 0.55rem;
		padding-top: 0.55rem;
	}

	.tool-stack {
		display: grid;
		gap: 0.55rem;
	}

	.tool-trigger {
		display: grid;
		grid-template-columns: auto 1fr auto auto;
		align-items: center;
		gap: 0.55rem;
		border: 0;
		background: transparent;
		text-align: left;
	}

	.tool-dot {
		width: 0.45rem;
		height: 0.45rem;
		border-radius: 999px;
		background: var(--gold);
	}

	pre,
	code {
		font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
	}

	pre {
		margin: 0;
		overflow: auto;
		white-space: pre-wrap;
		color: #d4d4d8;
		font-size: 0.72rem;
		line-height: 1.55;
	}

	.action-button,
	.position-pill {
		padding: 0.28rem 0.5rem;
		font-size: 0.68rem;
	}

	.message-actions[data-role='user'] {
		justify-content: flex-end;
	}

	.workspace-rail {
		width: 42rem;
		flex: 0 0 auto;
		min-height: 0;
		display: flex;
		flex-direction: column;
		border-left: 1px solid var(--border);
		background: rgba(7, 7, 8, 0.82);
	}

	.transcript-collapsed .workspace-rail {
		width: auto;
		flex: 1 1 auto;
	}

	.workspace-surface-shell {
		flex: 1;
		min-height: 0;
		overflow: auto;
		padding: 1rem;
	}

	.workspace-surface {
		min-height: 100%;
		border: 1px solid var(--border);
		border-radius: 8px;
		background: rgba(12, 12, 15, 0.42);
		padding: 1rem;
		display: grid;
		align-content: start;
		gap: 0.75rem;
	}

	.workspace-surface h2 {
		font-size: 0.88rem;
		letter-spacing: 0;
		text-transform: none;
		color: white;
	}

	.workspace-surface p,
	.workspace-surface code {
		color: var(--muted);
		font-size: 0.78rem;
		overflow-wrap: anywhere;
	}

	.surface-status {
		border: 1px solid var(--border);
		border-radius: 999px;
		padding: 0.22rem 0.55rem;
		color: var(--dim);
		font-size: 0.64rem;
		text-transform: uppercase;
		letter-spacing: 0.12em;
	}

	.surface-status[data-active='true'] {
		border-color: rgba(224, 169, 109, 0.38);
		color: var(--gold);
	}

	.surface-grid {
		display: grid;
		grid-template-columns: repeat(2, minmax(0, 1fr));
		gap: 0.75rem;
	}

	.surface-grid section,
	.surface-activity {
		display: grid;
		gap: 0.4rem;
		border: 1px solid rgba(30, 30, 36, 0.75);
		border-radius: 8px;
		background: rgba(7, 7, 8, 0.35);
		padding: 0.75rem;
	}

	.surface-label {
		color: var(--dim);
		font-size: 0.62rem;
		letter-spacing: 0.14em;
		text-transform: uppercase;
	}

	.surface-activity {
		margin-top: 0.25rem;
	}

	.surface-activity-list {
		display: grid;
		gap: 0.5rem;
	}

	.surface-activity-row {
		display: grid;
		grid-template-columns: auto minmax(0, 1fr);
		gap: 0.5rem;
		align-items: start;
	}

	.surface-activity-row > span:last-child {
		min-width: 0;
		display: grid;
		gap: 0.1rem;
	}

	.surface-activity-row strong,
	.surface-activity-row small {
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.surface-activity-row strong {
		color: #d4d4d8;
		font-size: 0.72rem;
	}

	.surface-activity-row small {
		color: var(--dim);
		font-size: 0.66rem;
	}

	.composer-area {
		flex: 0 0 auto;
		display: grid;
		grid-template-columns: minmax(0, 1fr) minmax(14rem, 1fr);
		gap: 1rem;
		padding: 1rem 2rem;
		border-top: 1px solid var(--border);
		background: var(--bg);
	}

	.composer-card,
	.monitor-card {
		height: 7.25rem;
		min-height: 7.25rem;
		border: 1px solid var(--border);
		border-radius: 8px;
		background: #0e0e11;
		overflow: hidden;
	}

	.composer-card :global([data-chat-input-root]) {
		height: 100%;
		display: flex;
		flex-direction: column;
	}

	.composer-card :global(textarea) {
		flex: 1;
		min-height: 0;
		width: 100%;
		resize: none;
		border: 0;
		outline: none;
		background: transparent;
		color: white;
		padding: 0.75rem;
		font-size: 0.78rem;
	}

	.composer-bottom {
		border-top: 1px solid rgba(30, 30, 36, 0.65);
		padding: 0.45rem 0.6rem;
	}

	.composer-bottom :global([data-chat-input-bottom]) {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 0.75rem;
	}

	.model-strip {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		min-width: 0;
		color: var(--muted);
		font-size: 0.68rem;
	}

	.model-strip span {
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.send-button,
	.stop-button,
	.primary {
		background: white;
		color: black;
		border-color: white;
	}

	.stop-button {
		background: rgba(239, 68, 68, 0.12);
		border-color: rgba(239, 68, 68, 0.28);
		color: var(--red);
	}

	.monitor-card {
		position: relative;
		padding: 0.85rem 3rem 0.85rem 0.85rem;
	}

	.monitor-toggle {
		position: absolute;
		top: 0.5rem;
		right: 0.5rem;
	}

	.monitor-content {
		height: 100%;
		min-height: 0;
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
		color: var(--dim);
		font-size: 0.72rem;
	}

	.monitor-content strong {
		color: var(--gold);
		font-size: 0.78rem;
	}

	.monitor-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 0.75rem;
		flex: 0 0 auto;
	}

	.monitor-header span {
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
		font-size: 0.66rem;
		color: var(--dim);
	}

	.monitor-feed {
		min-height: 0;
		overflow: auto;
		display: grid;
		gap: 0.35rem;
		padding-right: 0.15rem;
	}

	.monitor-row {
		display: grid;
		grid-template-columns: auto minmax(0, 1fr);
		align-items: start;
		gap: 0.45rem;
		min-height: 1.25rem;
	}

	.monitor-dot {
		width: 0.45rem;
		height: 0.45rem;
		margin-top: 0.3rem;
		border-radius: 999px;
		background: var(--dim);
	}

	.monitor-row[data-status='active'] .monitor-dot {
		background: var(--gold);
		animation: blink 1.3s ease-in-out infinite;
	}

	.monitor-row[data-status='waiting'] .monitor-dot {
		background: #fbbf24;
		animation: blink 1.3s ease-in-out infinite;
	}

	.monitor-row[data-status='complete'] .monitor-dot {
		background: var(--green);
	}

	.monitor-row[data-status='error'] .monitor-dot {
		background: var(--red);
	}

	.monitor-row-copy {
		min-width: 0;
		display: grid;
		gap: 0.08rem;
	}

	.monitor-row-copy span,
	.monitor-row-copy small {
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.monitor-row-copy span {
		color: #d4d4d8;
		font-size: 0.68rem;
	}

	.monitor-row-copy small {
		color: var(--dim);
		font-size: 0.64rem;
	}

	.modal-backdrop {
		position: fixed;
		inset: 0;
		z-index: 50;
		display: flex;
		align-items: center;
		justify-content: center;
		background: rgba(0, 0, 0, 0.62);
		backdrop-filter: blur(14px);
		padding: 1rem;
	}

	.settings-modal,
	.permission-modal {
		width: min(64rem, 100%);
		max-height: 86vh;
		overflow: hidden;
		display: flex;
		flex-direction: column;
		border: 1px solid var(--border);
		border-radius: 10px;
		background: var(--panel);
	}

	.permission-modal {
		width: min(34rem, 100%);
	}

	.modal-header {
		padding: 1rem;
		border-bottom: 1px solid var(--border);
		background: var(--bg);
	}

	.modal-header h2,
	.permission-modal h2 {
		color: white;
		font-size: 0.9rem;
		letter-spacing: 0;
		text-transform: none;
	}

	.modal-header p,
	.permission-modal p {
		margin-top: 0.25rem;
		color: var(--muted);
		font-size: 0.72rem;
	}

	.settings-grid {
		overflow: auto;
		padding: 1rem;
		display: grid;
		grid-template-columns: repeat(2, minmax(0, 1fr));
		gap: 1rem;
	}

	.settings-field {
		display: grid;
		gap: 0.45rem;
	}

	.settings-field.wide {
		grid-column: 1 / -1;
	}

	.field-label {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 1rem;
		color: var(--muted);
		font-size: 0.7rem;
		letter-spacing: 0.14em;
		text-transform: uppercase;
	}

	select,
	input,
	textarea {
		width: 100%;
		border: 1px solid var(--border);
		border-radius: 7px;
		background: #09090b;
		color: white;
		outline: none;
		padding: 0.6rem 0.7rem;
	}

	textarea {
		min-height: 5rem;
		resize: vertical;
	}

	.workspace-modal {
		width: min(56rem, 100%);
	}

	.workspace-root-editor {
		display: grid;
		gap: 0.8rem;
	}

	.workspace-root-row {
		display: grid;
		grid-template-columns: minmax(0, 1fr) minmax(0, 2fr) auto;
		gap: 0.75rem;
		align-items: end;
		padding: 0.75rem;
		border: 1px solid var(--border);
		border-radius: 8px;
		background: rgba(7, 7, 8, 0.35);
	}

	.root-actions {
		display: flex;
		gap: 0.45rem;
	}

	.permission-grid {
		display: grid;
		gap: 0.5rem;
	}

	.permission-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 1rem;
		border: 1px solid var(--border);
		border-radius: 7px;
		padding: 0.6rem;
	}

	.permission-row div {
		display: flex;
		gap: 0.35rem;
	}

	.permission-row button,
	.modal-actions button {
		padding: 0.45rem 0.65rem;
	}

	.permission-row button.active {
		border-color: rgba(224, 169, 109, 0.42);
		color: var(--gold);
	}

	.modal-actions {
		justify-content: flex-end;
		padding: 1rem;
		border-top: 1px solid var(--border);
		background: var(--bg);
	}

	.permission-modal {
		padding: 1rem;
		gap: 1rem;
	}

	.permission-layer {
		z-index: 70;
	}

	.permission-content-layer {
		position: fixed;
		z-index: 80;
		top: 50%;
		left: 50%;
		transform: translate(-50%, -50%);
	}

	.permission-modal pre {
		max-height: 18rem;
		padding: 0.8rem;
		border: 1px solid var(--border);
		border-radius: 7px;
		background: #050506;
	}

	.cursor {
		animation: blink 1s steps(2, start) infinite;
	}

	@keyframes blink {
		50% {
			opacity: 0;
		}
	}

	@media (max-width: 980px) {
		.workspace-shell {
			display: grid;
			grid-template-columns: auto minmax(0, 1fr);
		}

		.workspace-rail {
			width: auto;
		}

		.transcript-panel {
			display: none;
		}

		.composer-area {
			grid-template-columns: 1fr;
			padding: 0.75rem;
		}
	}
</style>
