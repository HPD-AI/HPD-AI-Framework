/**
 * ChatInput Browser Tests
 *
 * Comprehensive browser-based tests for the ChatInput component.
 * Tests ARIA attributes, data attributes, user interactions, and accessibility.
 */

import { userEvent, page } from 'vitest/browser';
import { expect, it, describe } from 'vitest';
import { render } from 'vitest-browser-svelte';
import ChatInputTest from './chat-input-test.svelte';

// Helper to wait for element existence
async function expectExists(locator: ReturnType<typeof page.getByTestId>) {
	await expect.element(locator).toBeInTheDocument();
}

describe('ChatInput', () => {
	describe('ARIA Attributes & Accessibility', () => {
		it('should have correct ARIA attributes on textarea', async () => {
			render(ChatInputTest, { props: {} });

			// The textarea itself has the data-testid
			const input = page.getByTestId('chat-input-input');
			await expect.element(input).toHaveAttribute('role', 'textbox');
			await expect.element(input).toHaveAttribute('aria-multiline', 'true');
		});

		it('should set aria-disabled when disabled', async () => {
			render(ChatInputTest, { props: { disabled: true } });

			const input = page.getByTestId('chat-input-input');
			await expect.element(input).toHaveAttribute('aria-disabled', 'true');
		});

		it('should have placeholder for accessibility', async () => {
			render(ChatInputTest, { props: { placeholder: 'Enter your message' } });

			const input = page.getByTestId('chat-input-input');
			await expect.element(input).toHaveAttribute('placeholder', 'Enter your message');
		});
	});

	describe('Data Attributes', () => {
		it('should have data-chat-input-root attribute', async () => {
			render(ChatInputTest, { props: {} });

			const root = page.getByTestId('chat-input');
			await expect.element(root).toHaveAttribute('data-chat-input-root');
		});

		it('should have data-empty when input is empty', async () => {
			render(ChatInputTest, { props: {} });

			const root = page.getByTestId('chat-input');
			await expect.element(root).toHaveAttribute('data-empty');
		});

		it('should NOT have data-empty when input has content', async () => {
			const component = render(ChatInputTest, { props: {} });

			const root = page.getByTestId('chat-input');
			const input = page.getByTestId('chat-input-input');

			// Initially empty
			await expect.element(root).toHaveAttribute('data-empty');

			// Type to add content
			const user = userEvent.setup();
			await user.click(input);
			await user.keyboard('Hello');

			// Should no longer be empty
			await expect.element(root).not.toHaveAttribute('data-empty');
		});

		it('should have data-disabled when disabled', async () => {
			render(ChatInputTest, { props: { disabled: true } });

			const root = page.getByTestId('chat-input');
			await expect.element(root).toHaveAttribute('data-disabled');
		});

		it('should have data-focused when input is focused', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, { props: {} });

			const input = page.getByTestId('chat-input-input');
			const root = page.getByTestId('chat-input');

			// Not focused initially
			await expect.element(root).not.toHaveAttribute('data-focused');

			// Focus the input
			await user.click(input);

			// Should have focused attribute
			await expect.element(root).toHaveAttribute('data-focused');
		});
	});

	describe('Component Parts', () => {
		it('should render Root component', async () => {
			render(ChatInputTest, { props: {} });
			await expectExists(page.getByTestId('chat-input'));
		});

		it('should render Input component', async () => {
			render(ChatInputTest, { props: {} });
			await expectExists(page.getByTestId('chat-input-input'));
		});

		it('should render Leading when showLeading=true', async () => {
			render(ChatInputTest, { props: { showLeading: true } });
			await expectExists(page.getByTestId('chat-input-leading'));
			await expectExists(page.getByTestId('attach-button'));
			await expectExists(page.getByTestId('voice-button'));
		});

		it('should NOT render Leading when showLeading=false', async () => {
			render(ChatInputTest, { props: { showLeading: false } });
			await expect.element(page.getByTestId('chat-input-leading')).not.toBeInTheDocument();
		});

		it('should render Trailing when showTrailing=true', async () => {
			render(ChatInputTest, { props: { showTrailing: true } });
			await expectExists(page.getByTestId('chat-input-trailing'));
			await expectExists(page.getByTestId('emoji-button'));
			await expectExists(page.getByTestId('send-button'));
		});

		it('should NOT render Trailing when showTrailing=false', async () => {
			render(ChatInputTest, { props: { showTrailing: false } });
			await expect.element(page.getByTestId('chat-input-trailing')).not.toBeInTheDocument();
		});

		it('should render Top when showTop=true', async () => {
			render(ChatInputTest, { props: { showTop: true } });
			await expectExists(page.getByTestId('chat-input-top'));
			await expectExists(page.getByTestId('top-content'));
		});

		it('should NOT render Top when showTop=false', async () => {
			render(ChatInputTest, { props: { showTop: false } });
			await expect.element(page.getByTestId('chat-input-top')).not.toBeInTheDocument();
		});

		it('should render Bottom when showBottom=true', async () => {
			render(ChatInputTest, { props: { showBottom: true } });
			await expectExists(page.getByTestId('chat-input-bottom'));
			await expectExists(page.getByTestId('bottom-content'));
		});

		it('should NOT render Bottom when showBottom=false', async () => {
			render(ChatInputTest, { props: { showBottom: false } });
			await expect.element(page.getByTestId('chat-input-bottom')).not.toBeInTheDocument();
		});

		it('should render all parts when all flags are true', async () => {
			render(ChatInputTest, {
				props: {
					showTop: true,
					showLeading: true,
					showTrailing: true,
					showBottom: true
				}
			});

			await expectExists(page.getByTestId('chat-input-top'));
			await expectExists(page.getByTestId('chat-input-leading'));
			await expectExists(page.getByTestId('chat-input-input'));
			await expectExists(page.getByTestId('chat-input-trailing'));
			await expectExists(page.getByTestId('chat-input-bottom'));
		});
	});

	describe('Part Data Attributes', () => {
		it('should have data-chat-input-leading attribute', async () => {
			render(ChatInputTest, { props: { showLeading: true } });

			const leading = page.getByTestId('chat-input-leading');
			await expect.element(leading).toHaveAttribute('data-chat-input-leading');
		});

		it('should have data-chat-input-trailing attribute', async () => {
			render(ChatInputTest, { props: { showTrailing: true } });

			const trailing = page.getByTestId('chat-input-trailing');
			await expect.element(trailing).toHaveAttribute('data-chat-input-trailing');
		});

		it('should have data-chat-input-top attribute', async () => {
			render(ChatInputTest, { props: { showTop: true } });

			const top = page.getByTestId('chat-input-top');
			await expect.element(top).toHaveAttribute('data-chat-input-top');
		});

		it('should have data-chat-input-bottom attribute', async () => {
			render(ChatInputTest, { props: { showBottom: true } });

			const bottom = page.getByTestId('chat-input-bottom');
			await expect.element(bottom).toHaveAttribute('data-chat-input-bottom');
		});

		it('should have data-input attribute on textarea', async () => {
			render(ChatInputTest, { props: {} });

			const input = page.getByTestId('chat-input-input');
			await expect.element(input).toHaveAttribute('data-input');
		});
	});

	describe('State Propagation', () => {
		it('should propagate disabled state to accessories', async () => {
			render(ChatInputTest, {
				props: {
					disabled: true,
					showLeading: true,
					showTrailing: true
				}
			});

			const leading = page.getByTestId('chat-input-leading');
			const trailing = page.getByTestId('chat-input-trailing');

			await expect.element(leading).toHaveAttribute('data-disabled');
			await expect.element(trailing).toHaveAttribute('data-disabled');
		});

		it('should propagate focused state to accessories', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, {
				props: {
					showLeading: true,
					showTrailing: true
				}
			});

			const input = page.getByTestId('chat-input-input');
			const leading = page.getByTestId('chat-input-leading');
			const trailing = page.getByTestId('chat-input-trailing');

			// Not focused initially
			await expect.element(leading).not.toHaveAttribute('data-focused');
			await expect.element(trailing).not.toHaveAttribute('data-focused');

			// Focus
			await user.click(input);

			// Should propagate
			await expect.element(leading).toHaveAttribute('data-focused');
			await expect.element(trailing).toHaveAttribute('data-focused');
		});

		it('should propagate empty state to accessories', async () => {
			render(ChatInputTest, {
				props: {
					showLeading: true,
					showTrailing: true
				}
			});

			const leading = page.getByTestId('chat-input-leading');
			const trailing = page.getByTestId('chat-input-trailing');

			await expect.element(leading).toHaveAttribute('data-empty');
			await expect.element(trailing).toHaveAttribute('data-empty');
		});

		it('should update empty state when typing', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, {
				props: {
					showLeading: true,
					showTrailing: true
				}
			});

			const input = page.getByTestId('chat-input-input');
			const root = page.getByTestId('chat-input');

			// Initially empty
			await expect.element(root).toHaveAttribute('data-empty');

			// Type something
			await user.click(input);
			await user.keyboard('Hello');

			// Should no longer be empty
			await expect.element(root).not.toHaveAttribute('data-empty');
		});
	});

	describe('User Interactions', () => {
		it('should allow typing in the input', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, { props: {} });

			const input = page.getByTestId('chat-input-input');
			await user.click(input);
			await user.keyboard('Hello World');

			await expect.element(input).toHaveValue('Hello World');
		});

		it('should submit on Enter key', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, { props: {} });

			const input = page.getByTestId('chat-input-input');
			await user.click(input);
			await user.keyboard('Test message{Enter}');

			const submitCount = page.getByTestId('submit-count');
			await expect.element(submitCount).toHaveTextContent('1');

			const lastSubmitted = page.getByTestId('last-submitted');
			await expect.element(lastSubmitted).toHaveTextContent('Test message');
		});

		it('should NOT submit on Enter when empty', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, { props: {} });

			const input = page.getByTestId('chat-input-input');
			await user.click(input);
			await user.keyboard('{Enter}');

			const submitCount = page.getByTestId('submit-count');
			await expect.element(submitCount).toHaveTextContent('0');
		});

		it('should NOT submit on Enter when disabled', async () => {
			render(ChatInputTest, { props: { disabled: true } });

			const input = page.getByTestId('chat-input-input');

			// Input should be disabled
			await expect.element(input).toBeDisabled();

			const submitCount = page.getByTestId('submit-count');
			await expect.element(submitCount).toHaveTextContent('0');
		});

		it('should add newline on Shift+Enter', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, { props: {} });

			const input = page.getByTestId('chat-input-input');
			await user.click(input);
			await user.keyboard('Line 1{Shift>}{Enter}{/Shift}Line 2');

			// Check that the value contains both lines (newline preserved)
			const currentValue = page.getByTestId('current-value');
			await expect.element(currentValue).toHaveTextContent(/Line 1[\s\S]*Line 2/);
		});

		it('should submit via send button in trailing', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, {
				props: {
					showTrailing: true
				}
			});

			const input = page.getByTestId('chat-input-input');
			await user.click(input);
			await user.keyboard('Button submit');

			const sendButton = page.getByTestId('send-button');
			await user.click(sendButton);

			const submitCount = page.getByTestId('submit-count');
			await expect.element(submitCount).toHaveTextContent('1');

			const lastSubmitted = page.getByTestId('last-submitted');
			await expect.element(lastSubmitted).toHaveTextContent('Button submit');
		});

		it('should disable send button when input is empty', async () => {
			render(ChatInputTest, {
				props: {
					showTrailing: true
				}
			});

			const sendButton = page.getByTestId('send-button');
			await expect.element(sendButton).toBeDisabled();
		});

		it('should enable send button when input has content', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, {
				props: {
					showTrailing: true
				}
			});

			const input = page.getByTestId('chat-input-input');
			await user.click(input);
			await user.keyboard('Content');

			const sendButton = page.getByTestId('send-button');
			await expect.element(sendButton).not.toBeDisabled();
		});
	});

	describe('Snippet Props', () => {
		it('should expose characterCount to Bottom snippet', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, {
				props: {
					showBottom: true
				}
			});

			const input = page.getByTestId('chat-input-input');
			const charCount = page.getByTestId('char-count');

			// Initially 0
			await expect.element(charCount).toHaveTextContent('0 characters');

			// Type something
			await user.click(input);
			await user.keyboard('Hello');

			// Should update
			await expect.element(charCount).toHaveTextContent('5 characters');
		});

		it('should expose isEmpty to Bottom snippet', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, {
				props: {
					showBottom: true
				}
			});

			const input = page.getByTestId('chat-input-input');

			// Initially empty - hint should be visible
			await expectExists(page.getByTestId('hint'));

			// Type something
			await user.click(input);
			await user.keyboard('Hello');

			// Hint should disappear
			await expect.element(page.getByTestId('hint')).not.toBeInTheDocument();
		});

		it('should expose disabled state to accessories', async () => {
			render(ChatInputTest, {
				props: {
					disabled: true,
					showLeading: true
				}
			});

			const attachButton = page.getByTestId('attach-button');
			const voiceButton = page.getByTestId('voice-button');

			await expect.element(attachButton).toBeDisabled();
			await expect.element(voiceButton).toBeDisabled();
		});

		it('should expose submit function to accessories', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, {
				props: {
					showTrailing: true
				}
			});

			const input = page.getByTestId('chat-input-input');
			await user.click(input);
			await user.keyboard('Submit via snippet');

			const sendButton = page.getByTestId('send-button');
			await user.click(sendButton);

			const lastSubmitted = page.getByTestId('last-submitted');
			await expect.element(lastSubmitted).toHaveTextContent('Submit via snippet');
		});
	});

	describe('Value Management', () => {
		it('should update value when typing', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, { props: {} });

			const input = page.getByTestId('chat-input-input');
			const currentValue = page.getByTestId('current-value');

			await user.click(input);
			await user.keyboard('Test');

			await expect.element(currentValue).toHaveTextContent('Test');
		});
	});

	describe('Keyboard Navigation', () => {
		it('should be focusable via Tab', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, { props: {} });

			await user.tab();

			const root = page.getByTestId('chat-input');
			await expect.element(root).toHaveAttribute('data-focused');
		});

		it('should blur on Tab away', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, { props: {} });

			const input = page.getByTestId('chat-input-input');
			const root = page.getByTestId('chat-input');

			await user.click(input);
			await expect.element(root).toHaveAttribute('data-focused');

			await user.tab();
			await expect.element(root).not.toHaveAttribute('data-focused');
		});
	});

	describe('Disabled State', () => {
		it('should disable textarea when disabled=true', async () => {
			render(ChatInputTest, { props: { disabled: true } });

			const input = page.getByTestId('chat-input-input');
			await expect.element(input).toBeDisabled();
		});

		it('should enable textarea when disabled=false', async () => {
			render(ChatInputTest, { props: { disabled: false } });

			const input = page.getByTestId('chat-input-input');
			await expect.element(input).not.toBeDisabled();
		});

		it('should disable accessory buttons when disabled=true', async () => {
			render(ChatInputTest, {
				props: {
					disabled: true,
					showLeading: true,
					showTrailing: true
				}
			});

			const attachButton = page.getByTestId('attach-button');
			const voiceButton = page.getByTestId('voice-button');
			const emojiButton = page.getByTestId('emoji-button');
			const sendButton = page.getByTestId('send-button');

			await expect.element(attachButton).toBeDisabled();
			await expect.element(voiceButton).toBeDisabled();
			await expect.element(emojiButton).toBeDisabled();
			await expect.element(sendButton).toBeDisabled();
		});
	});

	describe('Edge Cases', () => {
		it('should handle emoji input', async () => {
			render(ChatInputTest, { props: {} });

			const input = page.getByTestId('chat-input-input');

			// Directly set the value to test emoji handling
			// Note: userEvent.keyboard() and userEvent.type() don't handle multi-byte unicode properly
			await input.fill('👋 Hello 🌍');

			// Verify the component state updated correctly
			const currentValue = page.getByTestId('current-value');
			await expect.element(currentValue).toHaveTextContent('👋 Hello 🌍');
		});

		it('should handle rapid typing', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, { props: {} });

			const input = page.getByTestId('chat-input-input');
			await user.click(input);

			// Rapid typing
			for (let i = 0; i < 10; i++) {
				await user.keyboard('a');
			}

			await expect.element(input).toHaveValue('aaaaaaaaaa');
		});

		it('should handle whitespace-only input', async () => {
			const user = userEvent.setup();
			render(ChatInputTest, { props: { showTrailing: true } });

			const input = page.getByTestId('chat-input-input');
			const sendButton = page.getByTestId('send-button');

			await user.click(input);
			await user.keyboard('   ');

			// Send button should still be disabled (whitespace-only)
			await expect.element(sendButton).toBeDisabled();
		});
	});
});
