/**
 * message-reactive-owner.svelte.test.ts
 *
 * Regression tests for the reactive-owner pitfall with MessageState.
 *
 * Root cause: constructing `new MessageState(message)` inside `$derived(...)`
 * orphans the instance — its `$state`/`$derived` fields have no reactive owner,
 * so they initialise with the constructor values but never update when the
 * underlying message changes.  The fix is to create the instance once at
 * component-script level and call `state.update(message)` inside `$effect`.
 *
 * These tests run in the browser project (vitest-browser) so that Svelte runes
 * (`$state`, `$derived`, `$effect`) are fully active.
 */

import { describe, it, expect } from 'vitest';
import { render } from 'vitest-browser-svelte';
import { page } from 'vitest/browser';
import { flushSync } from 'svelte';
import MessageReactiveOwnerHarness from './message-reactive-owner-harness.svelte';

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('MessageState — reactive owner safety', () => {
	/**
	 * When Message.Root is used inside {#each} (the normal chat usage), each
	 * message component must display the correct content from the initial render.
	 * This would silently show nothing if `state.snippetProps.content` returned
	 * '' due to an orphaned $state field.
	 */
	it('renders initial message content through the children snippet', async () => {
		render(MessageReactiveOwnerHarness, {
			props: { scenario: 'initial-content' }
		});

		const el = page.getByTestId('content-output');
		await expect.element(el).toHaveTextContent('Hello from the message');
	});

	it('renders initial role through the children snippet', async () => {
		render(MessageReactiveOwnerHarness, {
			props: { scenario: 'initial-role' }
		});

		const el = page.getByTestId('role-output');
		await expect.element(el).toHaveTextContent('assistant');
	});

	it('reflects streaming=true through the children snippet', async () => {
		render(MessageReactiveOwnerHarness, {
			props: { scenario: 'streaming' }
		});

		const el = page.getByTestId('streaming-output');
		await expect.element(el).toHaveTextContent('true');
	});

	it('reflects thinking=true through the children snippet', async () => {
		render(MessageReactiveOwnerHarness, {
			props: { scenario: 'thinking' }
		});

		const el = page.getByTestId('thinking-output');
		await expect.element(el).toHaveTextContent('true');
	});

	/**
	 * The primary regression: after a message prop update the component must
	 * re-render with new content.  The old `$derived(new MessageState(...))`
	 * pattern would create a fresh orphaned instance with the new constructor
	 * value but never propagate changes to the template because the $state
	 * signals had no owner tracking downstream effects.
	 */
	it('updates content when the message prop changes', async () => {
		const { rerender } = render(MessageReactiveOwnerHarness, {
			props: { scenario: 'update-content', content: 'First content' }
		});

		const el = page.getByTestId('content-output');
		await expect.element(el).toHaveTextContent('First content');

		await rerender({ scenario: 'update-content', content: 'Updated content' });

		await expect.element(el).toHaveTextContent('Updated content');
	});

	it('updates streaming state when the message prop changes', async () => {
		const { rerender } = render(MessageReactiveOwnerHarness, {
			props: { scenario: 'update-streaming', streaming: false }
		});

		const el = page.getByTestId('streaming-output');
		await expect.element(el).toHaveTextContent('false');

		await rerender({ scenario: 'update-streaming', streaming: true });

		await expect.element(el).toHaveTextContent('true');
	});

	/**
	 * data-message-id and data-role on the root element come from state.props,
	 * which is a plain getter reading $state fields.  With an orphaned instance
	 * these could be undefined/stale.
	 */
	it('applies data attributes from state.props to the root element', async () => {
		render(MessageReactiveOwnerHarness, {
			props: { scenario: 'data-attrs' }
		});

		const el = page.getByTestId('message-root');
		await expect.element(el).toHaveAttribute('data-message-id', 'test-id-123');
		await expect.element(el).toHaveAttribute('data-role', 'user');
		await expect.element(el).toHaveAttribute('data-status', 'complete');
	});
});
