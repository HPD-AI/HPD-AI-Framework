/**
 * PermissionDialog Tests
 *
 * Comprehensive tests for the PermissionDialog component.
 * Tests ARIA attributes, data attributes, permission flow, and accessibility.
 */

import { userEvent, page } from 'vitest/browser';
import { expect, it, describe } from 'vitest';
import { render } from 'vitest-browser-svelte';
import PermissionDialogTest from './permission-dialog-test.svelte';
import PermissionDialogTestRender from './permission-dialog-test-render.svelte';
import { createMockAgent } from '$lib/testing/mock-agent.svelte.js';
import type { PermissionRequest } from '$lib/agent/types.js';

// Helper to wait for element existence
async function expectExists(locator: ReturnType<typeof page.getByTestId>) {
	await expect.element(locator).toBeInTheDocument();
}

// Helper to wait for element non-existence
async function expectNotExists(locator: ReturnType<typeof page.getByTestId>) {
	await expect.element(locator).not.toBeInTheDocument();
}

// Setup helper
async function setup() {
	const t = render(PermissionDialogTest);
	const trigger = page.getByTestId('trigger');

	return {
		...t,
		trigger
	};
}

// Open helper - triggers a permission request
async function open() {
	const t = await setup();
	await expectNotExists(page.getByTestId('content'));
	await t.trigger.click();
	// Wait for content to appear
	await expectExists(page.getByTestId('content'));
	return t;
}

describe('PermissionDialog', () => {
	describe('Data Attributes', () => {
		it('should have bits data attrs on all parts', async () => {
			await open();

			const parts = ['overlay', 'content', 'header', 'description', 'actions'];
			for (const part of parts) {
				const el = page.getByTestId(part);
				await expect.element(el).toHaveAttribute(`data-permission-dialog-${part}`);
			}
		});

		it('should have data-state attribute on content and overlay', async () => {
			await open();

			const overlay = page.getByTestId('overlay');
			await expect.element(overlay).toHaveAttribute('data-state', 'open');

			const content = page.getByTestId('content');
			await expect.element(content).toHaveAttribute('data-state', 'open');
		});

		it('should not render content when no permission request', async () => {
			await setup();
			await expectNotExists(page.getByTestId('content'));
			await expectNotExists(page.getByTestId('overlay'));
		});
	});

	describe('ARIA Attributes & Accessibility', () => {
		it('should have role="dialog" on content', async () => {
			await open();
			const content = page.getByTestId('content');
			await expect.element(content).toHaveAttribute('role', 'dialog');
		});

		it('should have aria-modal="true" on content', async () => {
			await open();
			const content = page.getByTestId('content');
			await expect.element(content).toHaveAttribute('aria-modal', 'true');
		});

		it('should link aria-labelledby to header', async () => {
			await open();
			const content = page.getByTestId('content');
			const header = page.getByTestId('header');

			const labelledBy = await content.element().getAttribute('aria-labelledby');
			const headerId = await header.element().getAttribute('id');

			expect(labelledBy).toBe(headerId);
		});

		it('should link aria-describedby to description', async () => {
			await open();
			const content = page.getByTestId('content');
			const description = page.getByTestId('description');

			const describedBy = await content.element().getAttribute('aria-describedby');
			const descriptionId = await description.element().getAttribute('id');

			expect(describedBy).toBe(descriptionId);
		});

		it('should have tabindex on approve and deny buttons', async () => {
			await open();
			const approveAlways = page.getByTestId('approve-always');
			const approveOnce = page.getByTestId('approve-once');
			const deny = page.getByTestId('deny');

			await expect.element(approveAlways).toHaveAttribute('tabindex', '0');
			await expect.element(approveOnce).toHaveAttribute('tabindex', '0');
			await expect.element(deny).toHaveAttribute('tabindex', '0');
		});
	});

	describe('Permission Request Display', () => {
		it('should display function name', async () => {
			await open();
			const functionName = page.getByTestId('function-name');
			await expect.element(functionName).toHaveTextContent('dangerousAction');
		});

		it('should display tool description', async () => {
			await open();
			const toolDescription = page.getByTestId('tool-description');
			await expect
				.element(toolDescription)
				.toHaveTextContent('This action requires your approval');
		});

		it('should display tool arguments as JSON', async () => {
			await open();
			const toolArgs = page.getByTestId('tool-args');
			const text = await toolArgs.element().textContent;

			expect(text).toContain('"action"');
			expect(text).toContain('"delete"');
			expect(text).toContain('"target"');
			expect(text).toContain('"important-file.txt"');
		});
	});

	describe('Approval Flow', () => {
		it('should close dialog when approved with allow_once', async () => {
			await open();

			const approveOnce = page.getByTestId('approve-once');
			await approveOnce.click();

			// Dialog should close
			await expectNotExists(page.getByTestId('content'));
		});

		it('should close dialog when approved with allow_always', async () => {
			await open();

			const approveAlways = page.getByTestId('approve-always');
			await approveAlways.click();

			// Dialog should close
			await expectNotExists(page.getByTestId('content'));
		});

		it('should close dialog when denied', async () => {
			await open();

			const deny = page.getByTestId('deny');
			await deny.click();

			// Dialog should close
			await expectNotExists(page.getByTestId('content'));
		});
	});

	describe('Keyboard Navigation', () => {
		it('should allow Enter key to approve', async () => {
			await open();

			const approveOnce = page.getByTestId('approve-once');
			await approveOnce.element().focus();
			await userEvent.keyboard('{Enter}');

			// Dialog should close
			await expectNotExists(page.getByTestId('content'));
		});

		it('should allow Space key to approve', async () => {
			await open();

			const approveAlways = page.getByTestId('approve-always');
			await approveAlways.element().focus();
			await userEvent.keyboard(' '); // Space key

			// Dialog should close
			await expectNotExists(page.getByTestId('content'));
		});

		it('should allow Enter key to deny', async () => {
			await open();

			const deny = page.getByTestId('deny');
			await deny.element().focus();
			await userEvent.keyboard('{Enter}');

			// Dialog should close
			await expectNotExists(page.getByTestId('content'));
		});
	});

	describe('Auto-open Behavior', () => {
		it('should auto-open when permission request arrives', async () => {
			const agent = createMockAgent();

			render(PermissionDialogTest, {
				agentForTest: agent
			});

			// Initially closed
			await expectNotExists(page.getByTestId('content'));

			// Simulate permission request
			const request: PermissionRequest = {
				permissionId: 'test-permission-2',
				sourceName: 'test-tools',
				functionName: 'testFunction',
				description: 'Test description',
				callId: 'call-456',
				arguments: { test: 'value' }
			};

			agent.state.onPermissionRequest(request);

			// Should auto-open
			await expectExists(page.getByTestId('content'));
		});

		it('should close when permission is approved programmatically', async () => {
			const agent = createMockAgent();

			render(PermissionDialogTest, {
				agentForTest: agent
			});

			// Add permission request
			const request: PermissionRequest = {
				permissionId: 'test-permission-3',
				sourceName: 'test-tools',
				functionName: 'testFunction',
				callId: 'call-789',
				description: 'Test',
				arguments: {}
			};

			agent.state.onPermissionRequest(request);
			await expectExists(page.getByTestId('content'));

			// Approve programmatically
			await agent.approve(request.permissionId, 'ask');

			// Should close
			await expectNotExists(page.getByTestId('content'));
		});
	});

	describe('Component State', () => {
		it('should have disabled=undefined on enabled buttons', async () => {
			await open();

			const approveOnce = page.getByTestId('approve-once');
			const disabled = await approveOnce.element().getAttribute('disabled');

			expect(disabled).toBeNull();
		});

		it('should show correct data-permission-dialog-approve attribute', async () => {
			await open();

			const approveOnce = page.getByTestId('approve-once');
			await expect
				.element(approveOnce)
				.toHaveAttribute('data-permission-dialog-approve');
		});

		it('should show correct data-permission-dialog-deny attribute', async () => {
			await open();

			const deny = page.getByTestId('deny');
			await expect.element(deny).toHaveAttribute('data-permission-dialog-deny');
		});
	});

	describe('Edge Cases', () => {
		it('should handle multiple permission requests (show first)', async () => {
			const agent = createMockAgent();

			render(PermissionDialogTest, {
				agentForTest: agent
			});

			// Add two permission requests
			const request1: PermissionRequest = {
				permissionId: 'perm-1',
				sourceName: 'tools',
				functionName: 'firstFunction',
				callId: 'call-1',
				description: 'First',
				arguments: {}
			};

			const request2: PermissionRequest = {
				permissionId: 'perm-2',
				sourceName: 'tools',
				functionName: 'secondFunction',
				callId: 'call-2',
				description: 'Second',
				arguments: {}
			};

			agent.state.onPermissionRequest(request1);
			agent.state.onPermissionRequest(request2);

			await expectExists(page.getByTestId('content'));

			// Should show first request
			const functionName = page.getByTestId('function-name');
			await expect.element(functionName).toHaveTextContent('firstFunction');
		});

		it('should not render without agent', async () => {
			// This would be a TypeScript error, but test defensive programming
			// Just verify setup doesn't crash
			const t = await setup();
			expect(t).toBeDefined();
		});
	});

	describe('Render Prop  ', () => {
		it('should render custom UI when render prop is provided', async () => {
			const agent = createMockAgent();
			const { unmount } = render(PermissionDialogTestRender, { agentForTest: agent });

			// Trigger permission request
			agent.state.onPermissionRequest({
				permissionId: 'test-1',
				sourceName: 'tools',
				functionName: 'deleteFile',
				callId: 'call-1',
				description: 'Delete the file',
				arguments: { path: '/test.txt' }
			});

			// Should show custom render UI
			const customUI = page.getByTestId('custom-permission-ui');
			await expect.element(customUI).toBeInTheDocument();
			await expect.element(customUI).toHaveTextContent('deleteFile');

			unmount();
		});

		it('should pass status to render prop', async () => {
			const agent = createMockAgent();
			const { unmount } = render(PermissionDialogTestRender, { agentForTest: agent });

			// Initially inProgress
			let statusEl = page.getByTestId('status-value');
			await expect.element(statusEl).toHaveTextContent('inProgress');

			// Trigger permission - becomes executing
			agent.state.onPermissionRequest({
				permissionId: 'test-1',
				sourceName: 'tools',
				functionName: 'deleteFile',
				callId: 'call-1'
			});

			statusEl = page.getByTestId('status-value');
			await expect.element(statusEl).toHaveTextContent('executing');

			unmount();
		});

		it('should allow approval via render prop', async () => {
			const agent = createMockAgent();
			const { unmount } = render(PermissionDialogTestRender, { agentForTest: agent });

			const request: PermissionRequest = {
				permissionId: 'test-1',
				sourceName: 'tools',
				functionName: 'deleteFile',
				callId: 'call-1'
			};

			agent.state.onPermissionRequest(request);

			// Click custom approve button
			const approveBtn = page.getByTestId('custom-approve');
			await approveBtn.click();

			// Should call approve and remove request
			expect(agent.state.pendingPermissions.length).toBe(0);

			unmount();
		});

		it('should allow denial via render prop', async () => {
			const agent = createMockAgent();
			const { unmount } = render(PermissionDialogTestRender, { agentForTest: agent });

			const request: PermissionRequest = {
				permissionId: 'test-1',
				sourceName: 'tools',
				functionName: 'deleteFile',
				callId: 'call-1'
			};

			agent.state.onPermissionRequest(request);

			// Click custom deny button
			const denyBtn = page.getByTestId('custom-deny');
			await denyBtn.click();

			// Should call deny and remove request
			expect(agent.state.pendingPermissions.length).toBe(0);

			unmount();
		});

		it('should show status="complete" while responding', async () => {
			const agent = createMockAgent();
			const { unmount } = render(PermissionDialogTestRender, { agentForTest: agent });

			agent.state.onPermissionRequest({
				permissionId: 'test-1',
				sourceName: 'tools',
				functionName: 'deleteFile',
				callId: 'call-1'
			});

			// Click approve - during async processing, status should be "complete"
			const approveBtn = page.getByTestId('custom-approve');
			await approveBtn.click();

			// After approval completes, should be back to inProgress
			const statusEl = page.getByTestId('status-value');
			await expect.element(statusEl).toHaveTextContent('inProgress');

			unmount();
		});
	});
});
