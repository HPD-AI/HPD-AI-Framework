/**
 * MessageList State Management
 *
 * Container for chat messages with auto-scrolling and keyboard navigation.
 *
 * Auto-scroll modes:
 *   'bottom'       — scroll to end on every change (classic chat).
 *   'sent-message' — scroll so the latest user message is at the top of the
 *                    viewport; the assistant response streams below it.
 *   'none'         — no automatic scrolling.
 *
 * In both active modes, auto-scroll is suppressed while the user has scrolled
 * up (more than `atBottomThreshold` px from the bottom).  It resumes once they
 * return to the bottom.
 */

import { Context } from 'runed';
import { type ReadableBoxedValues, type WritableBoxedValues, onDestroyEffect } from 'svelte-toolbelt';
import type { Message } from '../agent/types.ts';
import type { ScrollBehavior } from './types.js';
import { SvelteMap } from 'svelte/reactivity';
import { debounce } from '$lib/internal/debounce.js';

const MessageListRootContext = new Context<MessageListState>('MessageList.Root');

interface MessageListStateOpts
	extends ReadableBoxedValues<{
			messages: Message[];
			scrollBehavior: ScrollBehavior;
			atBottomThreshold: number;
			keyboardNav: boolean;
			ariaLabel: string;
			id: string | undefined | null;
		}>,
		WritableBoxedValues<{ ref: HTMLDivElement | null }> {}

export class MessageListState {
	readonly opts: MessageListStateOpts;

	#listNode = $state<HTMLDivElement | null>(null);

	/**
	 * Reactive message count — backed by $state so that $derived computations
	 * that read `messageCount` (e.g. snippetProps in message-list.svelte) re-run
	 * whenever messages are mutated in-place, not only when the array reference changes.
	 * Updated unconditionally at the top of the scroll $effect, before any early returns.
	 */
	#messageCount = $state(0);

	/** True when the scroll container is within `atBottomThreshold` of the bottom. */
	#isAtBottom = $state(true);

	/**
	 * Registry of message-id → DOM element.
	 * Populated by consumers via registerMessageElement / unregisterMessageElement.
	 */
	#messageElements = new SvelteMap<string, HTMLElement>();

	/** The id of the most recent user-role message — used for 'sent-message' mode. */
	#lastUserMessageId = $state<string | null>(null);

	// -------------------------------------------------------------------------
	// Scroll helpers
	// -------------------------------------------------------------------------

	#scrollToBottomImmediate = debounce(() => {
		const node = this.#listNode;
		if (!node) return;
		node.scrollTop = node.scrollHeight;
	}, 16);

	#scrollToSentMessageImmediate = debounce(() => {
		const node = this.#listNode;
		const id = this.#lastUserMessageId;
		if (!node || !id) return;

		const el = this.#messageElements.get(id);
		if (el) {
			// Align the message to the top of the scroll container.
			const containerTop = node.getBoundingClientRect().top;
			const elTop = el.getBoundingClientRect().top;
			node.scrollTop += elTop - containerTop;
		} else {
			// Element not registered yet — fall back to bottom.
			node.scrollTop = node.scrollHeight;
		}
	}, 16);

	// -------------------------------------------------------------------------
	// Scroll position tracking
	// -------------------------------------------------------------------------

	#handleScroll = () => {
		const node = this.#listNode;
		if (!node) return;
		const distanceFromBottom = node.scrollHeight - node.scrollTop - node.clientHeight;
		this.#isAtBottom = distanceFromBottom <= this.opts.atBottomThreshold.current;
	};

	// -------------------------------------------------------------------------
	// Constructor
	// -------------------------------------------------------------------------

	/** Bind this to the scroll container via `bind:this={listState.setRef}`. */
	readonly setRef = (v: HTMLDivElement | null) => {
		if (this.#listNode) {
			this.#listNode.removeEventListener('scroll', this.#handleScroll);
		}
		this.#listNode = v;
		this.opts.ref.current = v;
		if (v) {
			v.addEventListener('scroll', this.#handleScroll, { passive: true });
			this.#handleScroll();
		}
	};

	constructor(opts: MessageListStateOpts) {
		this.opts = opts;

		// -----------------------------------------------------------------------
		// Detect new user messages and drive auto-scroll.
		// -----------------------------------------------------------------------
		$effect(() => {
			const messages = this.opts.messages.current;
			const behavior = this.opts.scrollBehavior.current;

			// Update BEFORE early returns so snippetProps $derived always has a reactive
			// dependency on the message count, even when the list node isn't mounted yet.
			this.#messageCount = messages.length;

			if (behavior === 'none' || !this.#listNode || messages.length === 0) return;

			// Find the last user message.
			let newLastUserId: string | null = null;
			for (let i = messages.length - 1; i >= 0; i--) {
				if (messages[i].role === 'user') {
					newLastUserId = messages[i].id;
					break;
				}
			}

			const userMessageChanged = newLastUserId !== null && newLastUserId !== this.#lastUserMessageId;

			if (userMessageChanged) {
				// A new user message was just appended.
				this.#lastUserMessageId = newLastUserId;

				// Always scroll for a new send, regardless of current position.
				if (behavior === 'sent-message') {
					this.#scrollToSentMessageImmediate();
				} else {
					this.#scrollToBottomImmediate();
				}
				return;
			}

			// Streaming / assistant updates — only scroll if already at bottom.
			if (this.#isAtBottom) {
				if (behavior === 'sent-message') {
					// In sent-message mode keep scrolling to bottom during streaming
					// so the response naturally comes into view below the anchor.
					this.#scrollToBottomImmediate();
				} else {
					this.#scrollToBottomImmediate();
				}
			}
		});

		onDestroyEffect(() => {
			this.#scrollToBottomImmediate.destroy();
			this.#scrollToSentMessageImmediate.destroy();
			if (this.#listNode) {
				this.#listNode.removeEventListener('scroll', this.#handleScroll);
			}
		});
	}

	// -------------------------------------------------------------------------
	// Public API
	// -------------------------------------------------------------------------

	get messageCount() {
		return this.#messageCount;
	}

	get isAtBottom() {
		return this.#isAtBottom;
	}

	/** Imperatively scroll to the bottom. Use this for a "jump to bottom" button. */
	readonly scrollToBottom = () => {
		const node = this.#listNode;
		if (!node) return;
		node.scrollTop = node.scrollHeight;
		this.#isAtBottom = true;
	};

	/** Register a message element for 'sent-message' scroll targeting. */
	readonly registerMessageElement = (id: string, el: HTMLElement) => {
		this.#messageElements.set(id, el);
	};

	/** Unregister a message element when it leaves the DOM. */
	readonly unregisterMessageElement = (id: string) => {
		this.#messageElements.delete(id);
	};

	// -------------------------------------------------------------------------
	// Static factory
	// -------------------------------------------------------------------------

	static create(opts: MessageListStateOpts): MessageListState {
		return MessageListRootContext.set(new MessageListState(opts));
	}

	// -------------------------------------------------------------------------
	// Rendered props
	// -------------------------------------------------------------------------

	readonly props = $derived.by(
		() =>
			({
				id: this.opts.id.current,
				'data-message-list': '',
				'data-message-count': this.messageCount,
				'data-at-bottom': this.#isAtBottom ? '' : undefined,
				role: 'log',
				'aria-label': this.opts.ariaLabel.current,
				'aria-live': 'polite',
				'aria-atomic': 'false',
				tabindex: this.opts.keyboardNav.current ? 0 : -1,
			}) as const
	);
}
