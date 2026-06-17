/**
 * MessageList Component Tests
 */

import { describe, it, expect } from 'vitest';
import { page } from 'vitest/browser';
import { render } from 'vitest-browser-svelte';
import MessageListTest from './message-list-test.svelte';
import type { Message, MessageRole } from '../../agent/types.ts';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeMessage(
	id: string,
	role: MessageRole = 'user',
	content = `Message ${id}`,
): Message {
	return {
		id,
		role,
		content,
		streaming: false,
		thinking: false,
		reasoning: '',
		toolCalls: [],
		timestamp: new Date(),
	};
}

// ---------------------------------------------------------------------------
// ARIA & Accessibility
// ---------------------------------------------------------------------------

describe('MessageList', () => {
	describe('ARIA Attributes & Accessibility', () => {
		it('should have correct ARIA role and attributes', async () => {
			render(MessageListTest, { props: { messages: [] } });
			const list = page.getByTestId('message-list');
			await expect.element(list).toHaveAttribute('role', 'log');
			await expect.element(list).toHaveAttribute('aria-label', 'Message history');
			await expect.element(list).toHaveAttribute('aria-live', 'polite');
			await expect.element(list).toHaveAttribute('aria-atomic', 'false');
		});

		it('should support custom aria-label', async () => {
			render(MessageListTest, { props: { messages: [], 'aria-label': 'Chat conversation' } });
			const list = page.getByTestId('message-list');
			await expect.element(list).toHaveAttribute('aria-label', 'Chat conversation');
		});

		it('should have tabindex=0 when keyboard navigation enabled', async () => {
			render(MessageListTest, { props: { messages: [], keyboardNav: true } });
			const list = page.getByTestId('message-list');
			await expect.element(list).toHaveAttribute('tabindex', '0');
		});

		it('should have tabindex=-1 when keyboard navigation disabled', async () => {
			render(MessageListTest, { props: { messages: [], keyboardNav: false } });
			const list = page.getByTestId('message-list');
			await expect.element(list).toHaveAttribute('tabindex', '-1');
		});
	});

	// -------------------------------------------------------------------------
	// Data Attributes
	// -------------------------------------------------------------------------

	describe('Data Attributes', () => {
		it('should have data-message-list attribute', async () => {
			render(MessageListTest, { props: { messages: [] } });
			const list = page.getByTestId('message-list');
			await expect.element(list).toHaveAttribute('data-message-list');
		});

		it('should display message count', async () => {
			const messages = [makeMessage('1'), makeMessage('2', 'assistant')];
			render(MessageListTest, { props: { messages } });
			const list = page.getByTestId('message-list');
			await expect.element(list).toHaveAttribute('data-message-count', '2');
		});

		it('should update message count reactively', async () => {
			const messages = [makeMessage('1')];
			const { rerender } = render(MessageListTest, { props: { messages } });

			let list = page.getByTestId('message-list');
			await expect.element(list).toHaveAttribute('data-message-count', '1');

			await rerender({ messages: [...messages, makeMessage('2', 'assistant')] });
			list = page.getByTestId('message-list');
			await expect.element(list).toHaveAttribute('data-message-count', '2');
		});

		it('should have data-at-bottom attribute present when at bottom', async () => {
			render(MessageListTest, {
				props: { messages: [], onIsAtBottom: () => {} },
			});
			// Initial state is at-bottom=true
			const sentinel = page.getByTestId('at-bottom-sentinel');
			await expect.element(sentinel).toHaveAttribute('data-at-bottom', 'true');
		});
	});

	// -------------------------------------------------------------------------
	// Message Rendering
	// -------------------------------------------------------------------------

	describe('Message Rendering', () => {
		it('should render empty state with zero messages', async () => {
			render(MessageListTest, { props: { messages: [] } });
			const list = page.getByTestId('message-list');
			await expect.element(list).toHaveAttribute('data-message-count', '0');
		});

		it('should render multiple messages', async () => {
			const messages = Array.from({ length: 5 }, (_, i) =>
				makeMessage(`msg-${i}`, i % 2 === 0 ? 'user' : 'assistant'),
			);
			render(MessageListTest, { props: { messages } });
			const list = page.getByTestId('message-list');
			await expect.element(list).toHaveAttribute('data-message-count', '5');
		});

		it('should render message content', async () => {
			const messages = [makeMessage('1', 'user', 'Hello world')];
			render(MessageListTest, { props: { messages } });
			const msg = page.getByTestId('msg-1');
			await expect.element(msg).toHaveTextContent('Hello world');
		});
	});

	// -------------------------------------------------------------------------
	// Component Props
	// -------------------------------------------------------------------------

	describe('Component Props', () => {
		it('should accept and apply custom id', async () => {
			render(MessageListTest, { props: { messages: [], id: 'custom-message-list' } });
			const list = page.getByTestId('message-list');
			await expect.element(list).toHaveAttribute('id', 'custom-message-list');
		});

		it('should merge custom class names', async () => {
			render(MessageListTest, { props: { messages: [], class: 'custom-class' } });
			const list = page.getByTestId('message-list');
			await expect.element(list).toHaveClass('custom-class');
		});
	});

	// -------------------------------------------------------------------------
	// scrollBehavior prop
	// -------------------------------------------------------------------------

	describe('scrollBehavior prop', () => {
		it('defaults to "bottom"', async () => {
			// No scrollBehavior passed — should not throw and should render normally.
			const messages = [makeMessage('1'), makeMessage('2', 'assistant')];
			render(MessageListTest, { props: { messages } });
			const list = page.getByTestId('message-list');
			await expect.element(list).toHaveAttribute('data-message-count', '2');
		});

		it('accepts "sent-message" without throwing', async () => {
			const messages = [makeMessage('1'), makeMessage('2', 'assistant')];
			render(MessageListTest, { props: { messages, scrollBehavior: 'sent-message' } });
			const list = page.getByTestId('message-list');
			await expect.element(list).toHaveAttribute('data-message-count', '2');
		});

		it('accepts "none" without throwing', async () => {
			const messages = [makeMessage('1')];
			render(MessageListTest, { props: { messages, scrollBehavior: 'none' } });
			const list = page.getByTestId('message-list');
			await expect.element(list).toHaveAttribute('data-message-count', '1');
		});
	});

	// -------------------------------------------------------------------------
	// isAtBottom / scrollToBottom snippet props
	// -------------------------------------------------------------------------

	describe('isAtBottom snippet prop', () => {
		it('exposes isAtBottom=true initially (no overflow)', async () => {
			// With few messages there is no overflow so the container starts at-bottom.
			render(MessageListTest, {
				props: { messages: [makeMessage('1')], onIsAtBottom: () => {} },
			});
			const sentinel = page.getByTestId('at-bottom-sentinel');
			await expect.element(sentinel).toHaveAttribute('data-at-bottom', 'true');
		});
	});

	describe('scrollToBottom snippet prop', () => {
		it('exposes a scrollToBottom button that can be clicked', async () => {
			render(MessageListTest, {
				props: {
					messages: [makeMessage('1'), makeMessage('2', 'assistant')],
					onScrollToBottom: () => {},
				},
			});
			// Button must be present and clickable without error.
			const btn = page.getByTestId('scroll-to-bottom-btn');
			await expect.element(btn).toBeInTheDocument();
			await btn.click(); // Should not throw.
		});
	});

	// -------------------------------------------------------------------------
	// atBottomThreshold prop
	// -------------------------------------------------------------------------

	describe('atBottomThreshold prop', () => {
		it('accepts a custom threshold', async () => {
			render(MessageListTest, {
				props: { messages: [], atBottomThreshold: 100 },
			});
			const list = page.getByTestId('message-list');
			// Component should render correctly with custom threshold.
			await expect.element(list).toHaveAttribute('data-message-list');
		});
	});
});
