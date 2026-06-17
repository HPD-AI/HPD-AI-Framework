/**
 * Keyboard key constants for consistent key checking across components
 *
 * Use these constants instead of string literals for better type safety
 * and consistency across the library.
 *
 * @example
 * ```typescript
 * import { kbd } from '$lib/internal/kbd.js';
 *
 * onkeydown(e: KeyboardEvent) {
 *   if (e.key === kbd.ENTER && !e.shiftKey) {
 *     // Handle enter key
 *   }
 * }
 * ```
 */
export const kbd = {
	/** Enter key - commonly used for form submission */
	ENTER: 'Enter',

	/** Escape key - commonly used for dismissing dialogs/popovers */
	ESCAPE: 'Escape',

	/** Tab key - used for focus navigation */
	TAB: 'Tab',

	/** Space key - used for activating buttons/toggles */
	SPACE: ' ',

	/** Arrow Up key - used for list/menu navigation */
	ARROW_UP: 'ArrowUp',

	/** Arrow Down key - used for list/menu navigation */
	ARROW_DOWN: 'ArrowDown',

	/** Arrow Left key - used for horizontal navigation */
	ARROW_LEFT: 'ArrowLeft',

	/** Arrow Right key - used for horizontal navigation */
	ARROW_RIGHT: 'ArrowRight',

	/** Home key - navigate to start */
	HOME: 'Home',

	/** End key - navigate to end */
	END: 'End',

	/** Page Up key - scroll up by page */
	PAGE_UP: 'PageUp',

	/** Page Down key - scroll down by page */
	PAGE_DOWN: 'PageDown',

	/** Backspace key - delete previous character */
	BACKSPACE: 'Backspace',

	/** Delete key - delete next character */
	DELETE: 'Delete',
} as const;

/** Type representing valid keyboard keys from our kbd constants */
export type KbdKey = (typeof kbd)[keyof typeof kbd];
