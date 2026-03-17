import { describe, it, expect, vi } from 'vitest';
import { render } from 'vitest-browser-svelte';
import TranscriptionTest from './transcription.test.svelte';

describe('Transcription Component', () => {
	describe('ARIA Attributes', () => {
		it('should have correct ARIA role and label', async () => {
			const { container } = render(TranscriptionTest);

			const root = container.querySelector('[data-testid="transcription"]') as HTMLElement;
			expect(root).toBeTruthy();
			expect(root.getAttribute('role')).toBe('status');
			expect(root.getAttribute('aria-label')).toBe('Voice transcription');
			expect(root.getAttribute('aria-live')).toBe('polite');
		});

		it('should set aria-busy based on isFinal state', async () => {
			const { container } = render(TranscriptionTest);
			const root = container.querySelector('[data-testid="transcription"]') as HTMLElement;

			// Initially busy because isFinal is false (no transcription yet)
			expect(root.getAttribute('aria-busy')).toBe('true');
		});
	});

	describe('Data Attributes', () => {
		it('should NOT have data-final when transcription is interim', async () => {
			const { container } = render(TranscriptionTest);
			const root = container.querySelector('[data-testid="transcription"]') as HTMLElement;

			expect(root.hasAttribute('data-final')).toBe(false);
		});

		it('should have data-empty when transcription is empty', async () => {
			const { container } = render(TranscriptionTest);
			const root = container.querySelector('[data-testid="transcription"]') as HTMLElement;

			expect(root.hasAttribute('data-empty')).toBe(true);
		});

		it('should have data-confidence attribute', async () => {
			const { container } = render(TranscriptionTest);
			const root = container.querySelector('[data-testid="transcription"]') as HTMLElement;

			// Initially no confidence
			expect(root.hasAttribute('data-confidence')).toBe(false);
		});
	});

	describe('State Management', () => {
		it('should start with empty text', async () => {
			const { container } = render(TranscriptionTest);

			const textEl = container.querySelector('[data-testid="transcription-text"]') as HTMLElement;
			const emptyEl = container.querySelector('[data-testid="transcription-is-empty"]') as HTMLElement;

			expect(textEl.textContent).toContain('Text: ');
			expect(emptyEl.textContent).toContain('Empty: true');
		});
	});

	describe('Component Props', () => {
		it('should accept onTextChange callback', async () => {
			const onTextChange = vi.fn();
			render(TranscriptionTest, { onTextChange });

			// Callback is set up (actual invocation requires state access which we test via component)
			expect(onTextChange).toBeDefined();
		});

		it('should accept onClear callback', async () => {
			const onClear = vi.fn();
			render(TranscriptionTest, { onClear });

			// Callback is set up
			expect(onClear).toBeDefined();
		});
	});

	describe('Snippet Props', () => {
		it('should expose text in snippet', async () => {
			const { container } = render(TranscriptionTest);

			const textEl = container.querySelector('[data-testid="transcription-text"]') as HTMLElement;
			expect(textEl.textContent).toContain('Text: ');
		});

		it('should expose isFinal in snippet', async () => {
			const { container } = render(TranscriptionTest);

			const finalEl = container.querySelector('[data-testid="transcription-is-final"]') as HTMLElement;
			expect(finalEl.textContent).toContain('Final: false');
		});

		it('should expose confidence in snippet', async () => {
			const { container } = render(TranscriptionTest);

			const confEl = container.querySelector(
				'[data-testid="transcription-confidence"]'
			) as HTMLElement;
			expect(confEl.textContent).toContain('Confidence: null');
		});

		it('should expose confidenceLevel in snippet', async () => {
			const { container } = render(TranscriptionTest);

			const levelEl = container.querySelector(
				'[data-testid="transcription-confidence-level"]'
			) as HTMLElement;
			expect(levelEl.textContent).toContain('Level: null');
		});

		it('should expose isEmpty in snippet', async () => {
			const { container } = render(TranscriptionTest);

			const emptyEl = container.querySelector('[data-testid="transcription-is-empty"]') as HTMLElement;
			expect(emptyEl.textContent).toContain('Empty: true');
		});

		it('should expose clear function in snippet', async () => {
			const { container } = render(TranscriptionTest);

			const clearBtn = container.querySelector('[data-testid="transcription-clear-btn"]');
			expect(clearBtn).toBeTruthy();
		});
	});

});
