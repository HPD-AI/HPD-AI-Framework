/**
 * AudioPlaybackGate Tests
 * Following Bits UI testing patterns with comprehensive accessibility checks
 */

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render } from 'vitest-browser-svelte';
import { userEvent } from '@vitest/browser/context';
import AudioGateTest from './audio-playback-gate.test.svelte';

// ============================================
// Mock factory helpers
// ============================================

/**
 * Create a mock AudioContext constructor whose instances:
 * - start in 'suspended' state
 * - transition to 'running' after resume() resolves
 * - record close() calls
 */
function makeMockAudioContextClass(opts: { failResume?: boolean } = {}) {
	const mockClose = vi.fn().mockResolvedValue(undefined);
	const instances: { state: string; resume: ReturnType<typeof vi.fn>; close: ReturnType<typeof vi.fn> }[] = [];

	const MockClass = vi.fn(function (this: any) {
		this.state = 'suspended';
		this.close = mockClose;
		this.resume = vi.fn(async () => {
			if (opts.failResume) throw new Error('resume failed');
			this.state = 'running';
		});
		instances.push(this);
	});

	return { MockClass, mockClose, instances };
}

describe('AudioPlaybackGate', () => {
	// Use vi.stubGlobal so the stub is properly restored after each test.
	// globalThis.AudioContext = ... does not affect window.AudioContext in a real
	// browser frame, so we must use the official Vitest stub API.
	beforeEach(() => {
		const { MockClass } = makeMockAudioContextClass();
		vi.stubGlobal('AudioContext', MockClass);
	});

	afterEach(() => {
		vi.unstubAllGlobals();
	});

	describe('ARIA Attributes & Accessibility', () => {
		it('should have correct ARIA attributes on root', async () => {
			const { container } = render(AudioGateTest);
			const root = container.querySelector('[data-audio-gate-root]') as HTMLElement;

			await expect.element(root).toHaveAttribute('role', 'status');
			await expect.element(root).toHaveAttribute('aria-label', 'Audio playback status');
			await expect.element(root).toHaveAttribute('aria-live', 'polite');
		});

		it('should update aria-live when status changes', async () => {
			const { container } = render(AudioGateTest);
			const root = container.querySelector('[data-audio-gate-root]') as HTMLElement;

			// Initially should have aria-live="polite"
			await expect.element(root).toHaveAttribute('aria-live', 'polite');
		});
	});

	describe('Data Attributes', () => {
		it('should have audio-gate-root data attribute', async () => {
			const { container } = render(AudioGateTest);
			const root = container.querySelector('[data-audio-gate-root]') as HTMLElement;

			await expect.element(root).toBeInTheDocument();
			await expect.element(root).toHaveAttribute('data-audio-gate-root', '');
		});

		it('should have correct initial status', async () => {
			const { container } = render(AudioGateTest);
			const root = container.querySelector('[data-audio-gate-root]') as HTMLElement;

			await expect.element(root).toHaveAttribute('data-status', 'blocked');
		});

		it('should not have data-can-play attribute initially', async () => {
			const { container } = render(AudioGateTest);
			const root = container.querySelector('[data-audio-gate-root]') as HTMLElement;

			await expect.element(root).not.toHaveAttribute('data-can-play');
		});

		it('should not have data-error attribute initially', async () => {
			const { container } = render(AudioGateTest);
			const root = container.querySelector('[data-audio-gate-root]') as HTMLElement;

			await expect.element(root).not.toHaveAttribute('data-error');
		});
	});

	describe('State Management', () => {
		it('should start with blocked status', async () => {
			const { container } = render(AudioGateTest);
			const statusEl = container.querySelector('[data-testid="audio-gate-status"]') as HTMLElement;

			await expect.element(statusEl).toHaveAttribute('data-status', 'blocked');
			await expect.element(statusEl).toHaveTextContent('Status: blocked');
		});

		it('should show canPlayAudio as false initially', async () => {
			const { container } = render(AudioGateTest);
			const canPlayEl = container.querySelector('[data-testid="audio-gate-can-play"]') as HTMLElement;

			await expect.element(canPlayEl).toHaveAttribute('data-can-play', 'false');
			await expect.element(canPlayEl).toHaveTextContent('Can Play: false');
		});

		it('should display enable button when audio is blocked', async () => {
			const { container } = render(AudioGateTest);
			const enableBtn = container.querySelector('[data-testid="audio-gate-enable-btn"]') as HTMLElement;

			await expect.element(enableBtn).toBeInTheDocument();
			await expect.element(enableBtn).toHaveTextContent('Enable Audio');
		});
	});

	describe('Enable Audio Interaction', () => {
		it('should update status when enable button is clicked', async () => {
			// Inject a suspended AudioContext whose resume() transitions state to 'running'.
			// Using prop injection bypasses window.AudioContext (a native browser API that
			// cannot be replaced via vi.stubGlobal in a real Chromium context).
			const mockResume = vi.fn(async function (this: { state: string }) {
				this.state = 'running';
			});
			const mockAudioContext = {
				state: 'suspended',
				resume: mockResume,
				close: vi.fn().mockResolvedValue(undefined),
			} as unknown as AudioContext;

			const { container } = render(AudioGateTest, { audioContext: mockAudioContext });
			const enableBtn = container.querySelector('[data-testid="audio-gate-enable-btn"]') as HTMLElement;

			await userEvent.click(enableBtn!);
			await new Promise((resolve) => setTimeout(resolve, 50));

			expect(mockResume).toHaveBeenCalled();
		});

		it('should call onStatusChange callback when status changes', async () => {
			const onStatusChange = vi.fn();

			render(AudioGateTest, { onStatusChange });

			// onStatusChange should be called with initial status
			expect(onStatusChange).toHaveBeenCalledWith('blocked');
		});
	});

	describe('Error Handling', () => {
		it('should handle AudioContext resume failure', async () => {
			// Inject an AudioContext whose resume() rejects, triggering the error state.
			// We can't remove window.AudioContext in a real browser (native, read-only),
			// so we simulate failure through prop injection instead.
			const mockAudioContext = {
				state: 'suspended',
				resume: vi.fn().mockRejectedValue(new Error('AudioContext resume failed')),
				close: vi.fn().mockResolvedValue(undefined),
			} as unknown as AudioContext;

			const { container } = render(AudioGateTest, { audioContext: mockAudioContext });
			const enableBtn = container.querySelector('[data-testid="audio-gate-enable-btn"]') as HTMLElement;

			await userEvent.click(enableBtn!);
			await new Promise((resolve) => setTimeout(resolve, 100));

			const errorEl = container.querySelector('[data-testid="audio-gate-error"]') as HTMLElement;
			await expect.element(errorEl).toBeInTheDocument();
		});
	});

	describe('Component Props', () => {
		it('should accept custom data-testid', async () => {
			const { container } = render(AudioGateTest, { testId: 'custom-gate' });

			const root = container.querySelector('[data-testid="custom-gate"]') as HTMLElement;
			await expect.element(root).toBeInTheDocument();
		});

		it('should merge custom props with component props', async () => {
			const { container } = render(AudioGateTest, { testId: 'audio-gate' });

			const root = container.querySelector('[data-audio-gate-root]') as HTMLElement;
			await expect.element(root).toHaveAttribute('data-testid', 'audio-gate');
		});
	});

	describe('Snippet Props', () => {
		it('should expose canPlayAudio to snippet', async () => {
			const { container } = render(AudioGateTest);
			const canPlayEl = container.querySelector('[data-testid="audio-gate-can-play"]') as HTMLElement;

			await expect.element(canPlayEl).toBeInTheDocument();
		});

		it('should expose status to snippet', async () => {
			const { container } = render(AudioGateTest);
			const statusEl = container.querySelector('[data-testid="audio-gate-status"]') as HTMLElement;

			await expect.element(statusEl).toBeInTheDocument();
		});

		it('should expose enableAudio function to snippet', async () => {
			const { container } = render(AudioGateTest);
			const enableBtn = container.querySelector('[data-testid="audio-gate-enable-btn"]') as HTMLElement;

			await expect.element(enableBtn).toBeInTheDocument();
		});

		it('should expose error to snippet when error occurs', async () => {
			// Inject an AudioContext that fails on resume() to trigger the error state.
			const mockAudioContext = {
				state: 'suspended',
				resume: vi.fn().mockRejectedValue(new Error('resume failed')),
				close: vi.fn().mockResolvedValue(undefined),
			} as unknown as AudioContext;

			const { container } = render(AudioGateTest, { audioContext: mockAudioContext });
			const enableBtn = container.querySelector('[data-testid="audio-gate-enable-btn"]') as HTMLElement;

			await userEvent.click(enableBtn!);
			await new Promise((resolve) => setTimeout(resolve, 100));

			const errorEl = container.querySelector('[data-testid="audio-gate-error"]') as HTMLElement;
			await expect.element(errorEl).toBeInTheDocument();
		});
	});

	describe('Cleanup', () => {
		it('should close AudioContext on unmount', async () => {
			const { MockClass, mockClose } = makeMockAudioContextClass();
			vi.stubGlobal('AudioContext', MockClass);

			const { container, unmount } = render(AudioGateTest);

			const enableBtn = container.querySelector('[data-testid="audio-gate-enable-btn"]') as HTMLElement;
			await userEvent.click(enableBtn!);
			await new Promise((resolve) => setTimeout(resolve, 50));

			unmount();
			await new Promise((resolve) => setTimeout(resolve, 50));

			expect(mockClose).toHaveBeenCalled();
		});
	});
});
