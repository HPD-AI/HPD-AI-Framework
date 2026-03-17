/**
 * HPDOS Workspace Singleton
 *
 * Module-level reactive workspace — created once, imported everywhere.
 * Owns session list, branch management, and streaming state.
 */

import { createWorkspace, createExpandedToolKit, createSuccessResponse, createErrorResponse } from '@hpd/hpd-agent-headless-ui';
import { buildAppRecorderToolKit } from './apps/app-recorder/clientToolkit';
import { buildAppToolKit } from './apps/appToolkit';
import { AppRecorderState } from './apps/app-recorder/AppRecorderState.svelte';
import { appShellState } from './appShellState.svelte';

// Shared app-recorder state — one instance for the lifetime of the workspace.
// AppRecorderApp.svelte reads this same instance so agent tool calls update its UI.
export const appRecorderState = new AppRecorderState();

const STORAGE_KEY = 'hpdos:last-location';

function getBaseUrl() {
	const configured = (window as any).__HPDOS_API_BASE;
	const baseUrl = configured || window.location.origin;

	try {
		return new URL(baseUrl).toString().replace(/\/$/, '');
	} catch {
		throw new Error(`Invalid HPDOS API base URL: ${String(baseUrl)}`);
	}
}

function readSavedLocation(): { sessionId?: string; branchId?: string } {
	try {
		const raw = localStorage.getItem(STORAGE_KEY);
		return raw ? JSON.parse(raw) : {};
	} catch {
		return {};
	}
}

function saveLocation(sessionId: string | null, branchId: string | null) {
	if (sessionId && branchId) {
		localStorage.setItem(STORAGE_KEY, JSON.stringify({ sessionId, branchId }));
	}
}

const saved = readSavedLocation();

const artifactToolKit = createExpandedToolKit('artifacts', [
	{
		name: 'upsert_artifact',
		description: 'Create or update an artifact (code, HTML, or markdown) that renders inline in the chat.',
		parametersSchema: {
			type: 'object',
			properties: {
				id: {
					type: 'string',
					description: 'Stable unique identifier for this artifact. Use the same id to update an existing artifact.',
				},
				title: {
					type: 'string',
					description: 'Short display title shown in the artifact header.',
				},
				type: {
					type: 'string',
					enum: ['code', 'html', 'markdown'],
					description: 'Artifact type. Use "html" for rendered previews, "code" for source files, "markdown" for prose.',
				},
				language: {
					type: 'string',
					description: 'Programming language for syntax highlighting (e.g. "typescript", "python"). Only required when type is "code".',
				},
				content: {
					type: 'string',
					description: 'The full artifact content.',
				},
			},
			required: ['id', 'title', 'type', 'content'],
		},
	},
], {
	systemPrompt: `You have access to an \`upsert_artifact\` tool that renders content inline in the chat as an expandable card.

Use artifacts for: complete code files, HTML pages, markdown documents, or any standalone content the user may want to copy, run, or reference later.
Do NOT use artifacts for: short code snippets (< 15 lines), simple one-liners, or conversational text.

When creating an artifact:
- Choose a stable \`id\` based on the content (e.g. "spacex-landing-page", "sort-algorithm").
- Use the same \`id\` to update an existing artifact rather than creating a new one.
- Set \`type\` to "html" for anything that should be rendered as a live preview.`,
});

export const workspace = createWorkspace({
	baseUrl: getBaseUrl(),
	transport: 'sse',
	sessionId: saved.sessionId,
	initialBranchId: saved.branchId,
	clientToolKits: [artifactToolKit, buildAppToolKit(), buildAppRecorderToolKit()],
	onClientToolInvoke: async (request) => {
		if (request.toolName === 'upsert_artifact') {
			return createSuccessResponse(request.requestId, [{ type: 'text', text: 'Artifact created.' }]) as any;
		}
		if (request.toolName === 'open_app') {
			const { appId } = request.arguments as { appId: string };
			appShellState.openApp(appId);
			return createSuccessResponse(request.requestId, [{ type: 'text', text: `Opened app: ${appId}` }]) as any;
		}
		if (request.toolName === 'close_app') {
			appShellState.closeApp();
			return createSuccessResponse(request.requestId, [{ type: 'text', text: 'App panel closed.' }]) as any;
		}
		const appRecorderResult = appRecorderState.handleClientTool(request);
		if (appRecorderResult !== null) return appRecorderResult as any;
		return createErrorResponse(request.requestId, `Unknown client tool: ${request.toolName}`) as any;
	},
});

// Persist the active location whenever it changes
$effect.root(() => {
	$effect(() => {
		saveLocation(workspace.activeSessionId, workspace.activeBranchId);
	});
});
