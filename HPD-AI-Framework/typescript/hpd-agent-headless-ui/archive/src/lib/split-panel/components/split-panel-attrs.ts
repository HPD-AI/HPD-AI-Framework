/**
 * Split Panel Component Attributes
 *
 * Data attribute generators for split-panel component parts.
 * Provides consistent CSS selector patterns.
 */

import { createHPDAttrs } from '../../internal/index.js';

/**
 * Data attributes for split-panel component parts.
 * Used for styling and testing selectors.
 */
export const splitPanelAttrs = createHPDAttrs({
	component: 'split-panel',
	parts: ['root', 'split', 'pane', 'handle']
});
