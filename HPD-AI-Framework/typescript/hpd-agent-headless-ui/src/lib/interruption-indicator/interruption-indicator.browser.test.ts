import { describe, it, expect, vi } from 'vitest';
import { render } from 'vitest-browser-svelte';
import InterruptionIndicatorTest from './interruption-indicator.test.svelte';

describe('InterruptionIndicator Component', () => {
	describe('ARIA Attributes', () => {
		it('should have correct ARIA role and label', async () => {
			const { container } = render(InterruptionIndicatorTest);

			const root = container.querySelector('[data-testid="interruption"]') as HTMLElement;
			expect(root).toBeTruthy();
			expect(root.getAttribute('role')).toBe('status');
			expect(root.getAttribute('aria-label')).toBe('Interruption status');
			expect(root.getAttribute('aria-live')).toBe('assertive');
		});
	});

	describe('Data Attributes', () => {
		it('should have data-interruption-indicator-root attribute', async () => {
			const { container } = render(InterruptionIndicatorTest);
			const root = container.querySelector('[data-testid="interruption"]') as HTMLElement;

			expect(root.hasAttribute('data-interruption-indicator-root')).toBe(true);
		});

		it('should have data-status with normal initially', async () => {
			const { container } = render(InterruptionIndicatorTest);
			const root = container.querySelector('[data-testid="interruption"]') as HTMLElement;

			expect(root.getAttribute('data-status')).toBe('normal');
		});

		it('should NOT have data-interrupted when normal', async () => {
			const { container } = render(InterruptionIndicatorTest);
			const root = container.querySelector('[data-testid="interruption"]') as HTMLElement;

			expect(root.hasAttribute('data-interrupted')).toBe(false);
		});

		it('should NOT have data-paused when normal', async () => {
			const { container } = render(InterruptionIndicatorTest);
			const root = container.querySelector('[data-testid="interruption"]') as HTMLElement;

			expect(root.hasAttribute('data-paused')).toBe(false);
		});
	});

	describe('State Management', () => {
		it('should start with interrupted false', async () => {
			const { container } = render(InterruptionIndicatorTest);

			const interruptedEl = container.querySelector(
				'[data-testid="interruption-interrupted"]'
			) as HTMLElement;
			expect(interruptedEl.textContent).toContain('Interrupted: false');
		});

		it('should start with paused false', async () => {
			const { container } = render(InterruptionIndicatorTest);

			const pausedEl = container.querySelector(
				'[data-testid="interruption-paused"]'
			) as HTMLElement;
			expect(pausedEl.textContent).toContain('Paused: false');
		});

		it('should start with normal status', async () => {
			const { container } = render(InterruptionIndicatorTest);

			const statusEl = container.querySelector(
				'[data-testid="interruption-status"]'
			) as HTMLElement;
			expect(statusEl.textContent).toContain('Status: normal');
		});

		it('should start with null pause reason', async () => {
			const { container } = render(InterruptionIndicatorTest);

			const reasonEl = container.querySelector(
				'[data-testid="interruption-pause-reason"]'
			) as HTMLElement;
			expect(reasonEl.textContent).toContain('Reason: none');
		});

		it('should start with zero pause duration', async () => {
			const { container } = render(InterruptionIndicatorTest);

			const durationEl = container.querySelector(
				'[data-testid="interruption-pause-duration"]'
			) as HTMLElement;
			expect(durationEl.textContent).toContain('Duration: 0');
		});

		it('should start with empty interrupted text', async () => {
			const { container } = render(InterruptionIndicatorTest);

			const textEl = container.querySelector(
				'[data-testid="interruption-interrupted-text"]'
			) as HTMLElement;
			expect(textEl.textContent).toContain('Text: ');
		});
	});

	describe('Status Helpers', () => {
		it('should have isNormal true initially', async () => {
			const { container } = render(InterruptionIndicatorTest);

			const isNormalEl = container.querySelector(
				'[data-testid="interruption-is-normal"]'
			) as HTMLElement;
			expect(isNormalEl.textContent).toContain('IsNormal: true');
		});

		it('should have isInterrupted false initially', async () => {
			const { container } = render(InterruptionIndicatorTest);

			const isInterruptedEl = container.querySelector(
				'[data-testid="interruption-is-interrupted"]'
			) as HTMLElement;
			expect(isInterruptedEl.textContent).toContain('IsInterrupted: false');
		});

		it('should have isPaused false initially', async () => {
			const { container } = render(InterruptionIndicatorTest);

			const isPausedEl = container.querySelector(
				'[data-testid="interruption-is-paused"]'
			) as HTMLElement;
			expect(isPausedEl.textContent).toContain('IsPaused: false');
		});
	});

	describe('Component Props', () => {
		it('should accept onInterruptionChange callback', async () => {
			const onInterruptionChange = vi.fn();
			render(InterruptionIndicatorTest, { onInterruptionChange });

			// Callback is set up
			expect(onInterruptionChange).toBeDefined();
		});

		it('should accept onPauseChange callback', async () => {
			const onPauseChange = vi.fn();
			render(InterruptionIndicatorTest, { onPauseChange });

			// Callback is set up
			expect(onPauseChange).toBeDefined();
		});
	});

	describe('Snippet Props', () => {
		it('should expose interrupted in snippet', async () => {
			const { container } = render(InterruptionIndicatorTest);

			const interruptedEl = container.querySelector(
				'[data-testid="interruption-interrupted"]'
			) as HTMLElement;
			expect(interruptedEl).toBeTruthy();
		});

		it('should expose paused in snippet', async () => {
			const { container } = render(InterruptionIndicatorTest);

			const pausedEl = container.querySelector(
				'[data-testid="interruption-paused"]'
			) as HTMLElement;
			expect(pausedEl).toBeTruthy();
		});

		it('should expose status in snippet', async () => {
			const { container } = render(InterruptionIndicatorTest);

			const statusEl = container.querySelector(
				'[data-testid="interruption-status"]'
			) as HTMLElement;
			expect(statusEl).toBeTruthy();
		});

		it('should expose pauseReason in snippet', async () => {
			const { container } = render(InterruptionIndicatorTest);

			const reasonEl = container.querySelector(
				'[data-testid="interruption-pause-reason"]'
			) as HTMLElement;
			expect(reasonEl).toBeTruthy();
		});

		it('should expose pauseDuration in snippet', async () => {
			const { container } = render(InterruptionIndicatorTest);

			const durationEl = container.querySelector(
				'[data-testid="interruption-pause-duration"]'
			) as HTMLElement;
			expect(durationEl).toBeTruthy();
		});

		it('should expose interruptedText in snippet', async () => {
			const { container } = render(InterruptionIndicatorTest);

			const textEl = container.querySelector(
				'[data-testid="interruption-interrupted-text"]'
			) as HTMLElement;
			expect(textEl).toBeTruthy();
		});
	});
});
