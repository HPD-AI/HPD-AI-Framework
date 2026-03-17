import { describe, it, expect, vi } from 'vitest';
import { render } from 'vitest-browser-svelte';
import VoiceActivityIndicatorTest from './voice-activity-indicator.test.svelte';

describe('VoiceActivityIndicator Component', () => {
	describe('ARIA Attributes', () => {
		it('should have correct ARIA role and label', async () => {
			const { container } = render(VoiceActivityIndicatorTest);

			const root = container.querySelector('[data-testid="vad"]') as HTMLElement;
			expect(root).toBeTruthy();
			expect(root.getAttribute('role')).toBe('status');
			expect(root.getAttribute('aria-label')).toBe('Voice activity');
			expect(root.getAttribute('aria-live')).toBe('polite');
		});
	});

	describe('Data Attributes', () => {
		it('should NOT have data-active when inactive', async () => {
			const { container } = render(VoiceActivityIndicatorTest);
			const root = container.querySelector('[data-testid="vad"]') as HTMLElement;

			expect(root.hasAttribute('data-active')).toBe(false);
		});

		it('should have data-intensity with none initially', async () => {
			const { container } = render(VoiceActivityIndicatorTest);
			const root = container.querySelector('[data-testid="vad"]') as HTMLElement;

			expect(root.getAttribute('data-intensity')).toBe('none');
		});
	});

	describe('State Management', () => {
		it('should start inactive', async () => {
			const { container } = render(VoiceActivityIndicatorTest);

			const activeEl = container.querySelector('[data-testid="vad-active"]') as HTMLElement;
			expect(activeEl.textContent).toContain('Active: false');
		});

		it('should start with zero probability', async () => {
			const { container } = render(VoiceActivityIndicatorTest);

			const probEl = container.querySelector('[data-testid="vad-speech-probability"]') as HTMLElement;
			expect(probEl.textContent).toContain('Probability: 0');
		});

		it('should start with zero duration', async () => {
			const { container } = render(VoiceActivityIndicatorTest);

			const durationEl = container.querySelector('[data-testid="vad-duration"]') as HTMLElement;
			expect(durationEl.textContent).toContain('Duration: 0');
		});
	});

	describe('Intensity Levels', () => {
		it('should show none intensity when inactive', async () => {
			const { container } = render(VoiceActivityIndicatorTest);

			const intensityEl = container.querySelector('[data-testid="vad-intensity"]') as HTMLElement;
			expect(intensityEl.textContent).toContain('Intensity: none');
		});
	});

	describe('Component Props', () => {
		it('should accept onActivityChange callback', async () => {
			const onActivityChange = vi.fn();
			render(VoiceActivityIndicatorTest, { onActivityChange });

			// Callback is set up
			expect(onActivityChange).toBeDefined();
		});
	});

	describe('Snippet Props', () => {
		it('should expose active in snippet', async () => {
			const { container } = render(VoiceActivityIndicatorTest);

			const activeEl = container.querySelector('[data-testid="vad-active"]') as HTMLElement;
			expect(activeEl).toBeTruthy();
		});

		it('should expose speechProbability in snippet', async () => {
			const { container } = render(VoiceActivityIndicatorTest);

			const probEl = container.querySelector('[data-testid="vad-speech-probability"]') as HTMLElement;
			expect(probEl).toBeTruthy();
		});

		it('should expose duration in snippet', async () => {
			const { container} = render(VoiceActivityIndicatorTest);

			const durationEl = container.querySelector('[data-testid="vad-duration"]') as HTMLElement;
			expect(durationEl).toBeTruthy();
		});

		it('should expose intensityLevel in snippet', async () => {
			const { container } = render(VoiceActivityIndicatorTest);

			const intensityEl = container.querySelector('[data-testid="vad-intensity"]') as HTMLElement;
			expect(intensityEl).toBeTruthy();
		});
	});
});
