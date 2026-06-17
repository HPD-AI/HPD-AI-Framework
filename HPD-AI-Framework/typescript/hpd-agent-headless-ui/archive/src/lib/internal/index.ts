/**
 * Internal Utilities
 *
 * Low-level utilities for building headless UI components.
 * These utilities are used internally by ShellOS components.
 */

// Data attributes and styling
export {
	createHPDAttrs,
	boolToStr,
	boolToStrTrueOrUndef,
	boolToEmptyStrOrUndef,
	boolToTrueOrUndef,
	getDataOpenClosed,
	getDataChecked,
	getAriaChecked
} from './attrs.js';
export type { CreateHPDAttrsReturn } from './attrs.js';

// Keyboard constants
export { kbd } from './kbd.js';
export type { KbdKey } from './kbd.js';

// Common utilities
export { noop } from './noop.js';
export { createId } from './create-id.js';
export { debounce } from './debounce.js';

// Type utilities
export type {
	WithChild,
	Without,
	OnChangeFn,
	HPDKeyboardEvent,
	HPDMouseEvent,
	WithRefOpts,
	RefAttachment
} from './types.js';

// Re-export from svelte-toolbelt for convenience
export type { ReadableBoxedValues, WritableBoxedValues } from 'svelte-toolbelt';

// Focus management
export { RovingFocusGroup } from './roving-focus-group.js';
export {
	focus,
	focusFirst,
	focusWithoutScroll,
	getTabbableCandidates,
	getTabbableEdges,
	findVisible,
	handleCalendarInitialFocus
} from './focus.js';
export type { FocusableTarget } from './focus.js';

export { getTabbableFrom, getTabbableFromFocusable, isTabbable, isFocusable, tabbable, focusable } from './tabbable.js';

// Resize observer
export { HPDResizeObserver } from './svelte-resize-observer.svelte.js';

// Animation utilities
export { PresenceManager } from './presence-manager.svelte.js';
export { AnimationsComplete } from './animations-complete.js';

// DOM utilities
export { getFirstNonCommentChild, isClickTrulyOutside } from './dom.js';

// Event utilities
export { CustomEventDispatcher } from './events.js';
export type { EventCallback } from './events.js';

// Locale and direction
export { getElemDirection } from './locale.js';
export type { Direction } from './locale.js';

// Directional keys
export {
	getNextKey,
	getPrevKey,
	getDirectionalKeys,
	FIRST_KEYS,
	LAST_KEYS,
	FIRST_LAST_KEYS,
	SELECTION_KEYS
} from './get-directional-keys.js';
export type { Orientation } from './get-directional-keys.js';

// Type checking utilities
export {
	isBrowser,
	isIOS,
	isFunction,
	isHTMLElement,
	isElement,
	isElementOrSVGElement,
	isNumberString,
	isNull,
	isTouch,
	isFocusVisible,
	isNotNull,
	isSelectableInput,
	isElementHidden
} from './is.js';
