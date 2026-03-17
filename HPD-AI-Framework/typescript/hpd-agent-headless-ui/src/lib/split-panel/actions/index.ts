/**
 * SplitPanel Actions
 *
 * Svelte actions for panel registration and interaction.
 */

export {
	registerPanel,
	getPanelRect,
	getPanelElement,
	getRegisteredPanelIds,
	clearPanelRegistry
} from './register-panel.js';
export type { RegisterPanelParams } from './register-panel.js';
