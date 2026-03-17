import { createExpandedToolKit } from '@hpd/hpd-agent-headless-ui';
import { appRegistry } from './registry';

// ── Tool definitions ───────────────────────────────────────────────────────

const OPEN_APP = {
    name: 'open_app',
    description: 'Opens an app in the right panel by its app ID. The panel expands automatically.',
    parametersSchema: {
        type: 'object',
        properties: {
            appId: {
                type: 'string',
                description: 'The app ID to open (e.g. "hpd-video"). Must match one of the available apps.',
            },
        },
        required: ['appId'],
    },
} as const;

const CLOSE_APP = {
    name: 'close_app',
    description: 'Closes the currently open app and collapses the right panel.',
    parametersSchema: {
        type: 'object',
        properties: {},
        required: [],
    },
} as const;

// ── buildAppToolKit ────────────────────────────────────────────────────────

/**
 * Builds the app shell toolkit. Called at workspace init so the system prompt
 * reflects whatever apps are registered at that point.
 *
 * Adding a new AppManifest to the registry automatically makes it visible here —
 * no changes needed in workspace.svelte.ts.
 */
/**
 * Builds the additionalSystemInstructions string for the app shell.
 * Call this at send-time and pass via RunConfig so the agent always sees
 * the current registry — adding a new manifest is all that's needed.
 */
export function buildAppSystemInstructions(): string {
    const apps = appRegistry.list();
    const appList = apps.length > 0
        ? apps.map((a) => `- ${a.id}: ${a.name}${a.description ? ` — ${a.description}` : ''}`).join('\n')
        : '(no apps registered)';

    return `You can open and close apps in the HPDOS right panel using the open_app and close_app tools.

Available apps:
${appList}

Guidelines:
- Use open_app when the user asks to open, launch, or switch to an app.
- Use close_app when the user asks to close, hide, or dismiss the panel.
- Do not open an app unless the user clearly requests it or it is directly needed for the task.`;
}

export function buildAppToolKit() {
    return createExpandedToolKit('app-shell', [OPEN_APP, CLOSE_APP]);
}
