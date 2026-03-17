/**
 * SplitPanel Utilities
 *
 * Helper functions and registries for the split panel system.
 */

export {
	registerPanelDescriptor,
	unregisterPanelDescriptor,
	getPanelDescriptor,
	hasPanelDescriptor,
	clearPanelDescriptors,
	getRegisteredPanelTypes,
	defaultPanelDescriptor
} from './panel-descriptor-registry.js';

export {
	getActiveChildren,
	computeGridTemplate,
	encodeResizeHandle,
	decodeResizeHandle
} from './layout-rendering.js';

export type { ActiveChild } from './layout-rendering.js';

export {
	findPanelInDirection,
	collectPanelIds,
	hasOverlap,
	distanceBetweenRects
} from './keyboard-navigation.js';

export type { Direction } from './keyboard-navigation.js';
