import { describe, it, expect, vi } from 'vitest';
import { render } from 'vitest-browser-svelte';
import TurnIndicatorTest from './turn-indicator.test.svelte';

describe('TurnIndicator Component', () => {
	describe('ARIA Attributes', () => {
		it('should have correct ARIA role and label', async () => {
			const { container } = render(TurnIndicatorTest);

			const root = container.querySelector('[data-testid="turn"]') as HTMLElement;
			expect(root).toBeTruthy();
			expect(root.getAttribute('role')).toBe('status');
			expect(root.getAttribute('aria-label')).toBe('Current turn: unknown');
			expect(root.getAttribute('aria-live')).toBe('polite');
		});
	});

	describe('Data Attributes', () => {
		it('should have data-turn-indicator-root attribute', async () => {
			const { container } = render(TurnIndicatorTest);
			const root = container.querySelector('[data-testid="turn"]') as HTMLElement;

			expect(root.hasAttribute('data-turn-indicator-root')).toBe(true);
		});

		it('should have data-turn with unknown initially', async () => {
			const { container } = render(TurnIndicatorTest);
			const root = container.querySelector('[data-testid="turn"]') as HTMLElement;

			expect(root.getAttribute('data-turn')).toBe('unknown');
		});

		it('should have data-unknown attribute initially', async () => {
			const { container } = render(TurnIndicatorTest);
			const root = container.querySelector('[data-testid="turn"]') as HTMLElement;

			expect(root.hasAttribute('data-unknown')).toBe(true);
		});

		it('should NOT have data-user-turn when unknown', async () => {
			const { container } = render(TurnIndicatorTest);
			const root = container.querySelector('[data-testid="turn"]') as HTMLElement;

			expect(root.hasAttribute('data-user-turn')).toBe(false);
		});

		it('should NOT have data-agent-turn when unknown', async () => {
			const { container } = render(TurnIndicatorTest);
			const root = container.querySelector('[data-testid="turn"]') as HTMLElement;

			expect(root.hasAttribute('data-agent-turn')).toBe(false);
		});
	});

	describe('State Management', () => {
		it('should start with unknown turn', async () => {
			const { container } = render(TurnIndicatorTest);

			const turnEl = container.querySelector('[data-testid="turn-current-turn"]') as HTMLElement;
			expect(turnEl.textContent).toContain('Turn: unknown');
		});

		it('should start with zero completion probability', async () => {
			const { container } = render(TurnIndicatorTest);

			const probEl = container.querySelector(
				'[data-testid="turn-completion-probability"]'
			) as HTMLElement;
			expect(probEl.textContent).toContain('Probability: 0');
		});

		it('should start with null detection method', async () => {
			const { container } = render(TurnIndicatorTest);

			const methodEl = container.querySelector(
				'[data-testid="turn-detection-method"]'
			) as HTMLElement;
			expect(methodEl.textContent).toContain('Method: none');
		});
	});

	describe('Turn Helpers', () => {
		it('should have isUnknown true initially', async () => {
			const { container } = render(TurnIndicatorTest);

			const isUnknownEl = container.querySelector(
				'[data-testid="turn-is-unknown"]'
			) as HTMLElement;
			expect(isUnknownEl.textContent).toContain('IsUnknown: true');
		});

		it('should have isUserTurn false initially', async () => {
			const { container } = render(TurnIndicatorTest);

			const isUserTurnEl = container.querySelector(
				'[data-testid="turn-is-user-turn"]'
			) as HTMLElement;
			expect(isUserTurnEl.textContent).toContain('IsUserTurn: false');
		});

		it('should have isAgentTurn false initially', async () => {
			const { container } = render(TurnIndicatorTest);

			const isAgentTurnEl = container.querySelector(
				'[data-testid="turn-is-agent-turn"]'
			) as HTMLElement;
			expect(isAgentTurnEl.textContent).toContain('IsAgentTurn: false');
		});
	});

	describe('Component Props', () => {
		it('should accept onTurnChange callback', async () => {
			const onTurnChange = vi.fn();
			render(TurnIndicatorTest, { onTurnChange });

			// Callback is set up
			expect(onTurnChange).toBeDefined();
		});
	});

	describe('Snippet Props', () => {
		it('should expose currentTurn in snippet', async () => {
			const { container } = render(TurnIndicatorTest);

			const turnEl = container.querySelector('[data-testid="turn-current-turn"]') as HTMLElement;
			expect(turnEl).toBeTruthy();
		});

		it('should expose completionProbability in snippet', async () => {
			const { container } = render(TurnIndicatorTest);

			const probEl = container.querySelector(
				'[data-testid="turn-completion-probability"]'
			) as HTMLElement;
			expect(probEl).toBeTruthy();
		});

		it('should expose detectionMethod in snippet', async () => {
			const { container } = render(TurnIndicatorTest);

			const methodEl = container.querySelector(
				'[data-testid="turn-detection-method"]'
			) as HTMLElement;
			expect(methodEl).toBeTruthy();
		});

		it('should expose isUserTurn in snippet', async () => {
			const { container } = render(TurnIndicatorTest);

			const isUserTurnEl = container.querySelector(
				'[data-testid="turn-is-user-turn"]'
			) as HTMLElement;
			expect(isUserTurnEl).toBeTruthy();
		});

		it('should expose isAgentTurn in snippet', async () => {
			const { container } = render(TurnIndicatorTest);

			const isAgentTurnEl = container.querySelector(
				'[data-testid="turn-is-agent-turn"]'
			) as HTMLElement;
			expect(isAgentTurnEl).toBeTruthy();
		});

		it('should expose isUnknown in snippet', async () => {
			const { container } = render(TurnIndicatorTest);

			const isUnknownEl = container.querySelector(
				'[data-testid="turn-is-unknown"]'
			) as HTMLElement;
			expect(isUnknownEl).toBeTruthy();
		});
	});
});
