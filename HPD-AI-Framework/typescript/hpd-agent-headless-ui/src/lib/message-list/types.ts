/**
 * MessageList Component Types
 */

import type { HTMLAttributes } from 'svelte/elements';
import type { Message } from '../agent/types.ts';
import type { WithChild } from '$lib/internal/types.js';

/**
 * Controls how the list scrolls when new messages arrive.
 *
 * - `'bottom'`        — Always scroll to the very bottom (classic chat behavior).
 * - `'sent-message'`  — Scroll so the latest user message sits at the top of the
 *                       viewport; the assistant reply then streams into view below it.
 * - `'none'`          — No automatic scrolling; the consumer owns scroll position.
 */
export type ScrollBehavior = 'bottom' | 'sent-message' | 'none';

/**
 * Props for the MessageList component
 */
export type MessageListProps = MessageListPropsWithoutHTML &
	Omit<HTMLAttributes<HTMLDivElement>, keyof MessageListPropsWithoutHTML>;

/**
 * MessageList props without HTML attributes
 */
export type MessageListPropsWithoutHTML = WithChild<
	{
		/**
		 * Array of messages to display
		 */
		messages: Message[];

		/**
		 * Controls automatic scroll behavior when messages change.
		 *
		 * - `'bottom'`       — scroll to the end on every change (default).
		 * - `'sent-message'` — scroll so the latest user message is at the top
		 *                      of the viewport; the response streams below it.
		 * - `'none'`         — no automatic scrolling.
		 *
		 * In `'bottom'` and `'sent-message'` modes the list will NOT interrupt a
		 * user who has scrolled up — auto-scroll is suppressed until they return
		 * to the bottom (within `atBottomThreshold` px).
		 *
		 * @default 'bottom'
		 */
		scrollBehavior?: ScrollBehavior;

		/**
		 * Distance from the bottom (in px) within which the list is considered
		 * "at the bottom" for the purposes of auto-scroll suppression.
		 * @default 50
		 */
		atBottomThreshold?: number;

		/**
		 * Whether to enable keyboard navigation (arrow keys)
		 * @default true
		 */
		keyboardNav?: boolean;

		/**
		 * ARIA label for the message list
		 * @default "Message history"
		 */
		'aria-label'?: string;

		/**
		 * Element reference (bindable)
		 */
		ref?: HTMLDivElement | null;

		/**
		 * Unique ID for the message list
		 */
		id?: string;
	},
	MessageListSnippetProps
>;

/**
 * Props passed to snippet customization
 */
export type MessageListSnippetProps = {
	/**
	 * Array of messages being displayed
	 */
	messages: Message[];

	/**
	 * Number of messages in the list
	 */
	messageCount: number;

	/**
	 * Whether the scroll container is currently at (or near) the bottom.
	 * Use this to show/hide a "scroll to bottom" jump button.
	 */
	isAtBottom: boolean;

	/**
	 * Imperatively scroll to the bottom of the list.
	 * Useful for a "jump to bottom" button when `isAtBottom` is false.
	 */
	scrollToBottom: () => void;

	/**
	 * Register a message element so the list can scroll to it.
	 * Call this from each rendered message with its id and root element.
	 */
	registerMessageElement: (id: string, el: HTMLElement) => void;

	/**
	 * Unregister a message element when it is removed from the DOM.
	 */
	unregisterMessageElement: (id: string) => void;

	/**
	 * Wire the scroll container element into the list state.
	 * Use this with `bind:this` on the scroll container when using the `child` snippet:
	 * `<div bind:this={setRefEl}> ... </div>`
	 * combined with `$effect(() => { setRef(setRefEl); })`, or call directly.
	 */
	setRef: (el: HTMLDivElement | null) => void;
};
