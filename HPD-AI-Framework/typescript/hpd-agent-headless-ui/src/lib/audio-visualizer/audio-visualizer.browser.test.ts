import { describe, it, expect, vi } from 'vitest';
import { render } from 'vitest-browser-svelte';
import AudioVisualizerTest from './audio-visualizer.test.svelte';

describe('AudioVisualizer Component', () => {
	describe('ARIA Attributes', () => {
		it('should have correct ARIA role and label', async () => {
			const { container } = render(AudioVisualizerTest);

			const root = container.querySelector('[data-testid="visualizer"]') as HTMLElement;
			expect(root).toBeTruthy();
			expect(root.getAttribute('role')).toBe('img');
			expect(root.getAttribute('aria-label')).toBe('Audio visualization');
		});
	});

	describe('Data Attributes', () => {
		it('should have data-audio-visualizer-root attribute', async () => {
			const { container } = render(AudioVisualizerTest);
			const root = container.querySelector('[data-testid="visualizer"]') as HTMLElement;

			expect(root.hasAttribute('data-audio-visualizer-root')).toBe(true);
		});

		it('should have data-mode with bar initially', async () => {
			const { container } = render(AudioVisualizerTest);
			const root = container.querySelector('[data-testid="visualizer"]') as HTMLElement;

			expect(root.getAttribute('data-mode')).toBe('bar');
		});

		it('should have data-bands with 5 initially', async () => {
			const { container } = render(AudioVisualizerTest);
			const root = container.querySelector('[data-testid="visualizer"]') as HTMLElement;

			expect(root.getAttribute('data-bands')).toBe('5');
		});

		it('should NOT have data-active when not visualizing', async () => {
			const { container } = render(AudioVisualizerTest);
			const root = container.querySelector('[data-testid="visualizer"]') as HTMLElement;

			expect(root.hasAttribute('data-active')).toBe(false);
		});
	});

	describe('State Management', () => {
		it('should start with 5 bands by default', async () => {
			const { container } = render(AudioVisualizerTest);

			const bandsEl = container.querySelector('[data-testid="visualizer-bands"]') as HTMLElement;
			expect(bandsEl.textContent).toContain('Bands: 5');
		});

		it('should respect custom bands prop', async () => {
			const { container } = render(AudioVisualizerTest, { bands: 7 });

			const bandsEl = container.querySelector('[data-testid="visualizer-bands"]') as HTMLElement;
			expect(bandsEl.textContent).toContain('Bands: 7');
		});

		it('should start with bar mode by default', async () => {
			const { container } = render(AudioVisualizerTest);

			const modeEl = container.querySelector('[data-testid="visualizer-mode"]') as HTMLElement;
			expect(modeEl.textContent).toContain('Mode: bar');
		});

		it('should respect custom mode prop', async () => {
			const { container } = render(AudioVisualizerTest, { mode: 'waveform' });

			const modeEl = container.querySelector('[data-testid="visualizer-mode"]') as HTMLElement;
			expect(modeEl.textContent).toContain('Mode: waveform');
		});

		it('should start with zero max volume', async () => {
			const { container } = render(AudioVisualizerTest);

			const maxVolEl = container.querySelector(
				'[data-testid="visualizer-max-volume"]'
			) as HTMLElement;
			expect(maxVolEl.textContent).toContain('MaxVolume: 0');
		});

		it('should start with zero avg volume', async () => {
			const { container } = render(AudioVisualizerTest);

			const avgVolEl = container.querySelector(
				'[data-testid="visualizer-avg-volume"]'
			) as HTMLElement;
			expect(avgVolEl.textContent).toContain('AvgVolume: 0.00');
		});

		it('should start with isActive false', async () => {
			const { container } = render(AudioVisualizerTest);

			const isActiveEl = container.querySelector(
				'[data-testid="visualizer-is-active"]'
			) as HTMLElement;
			expect(isActiveEl.textContent).toContain('IsActive: false');
		});

		it('should initialize volumes array with zeros', async () => {
			const { container } = render(AudioVisualizerTest);

			const volumesEl = container.querySelector(
				'[data-testid="visualizer-volumes"]'
			) as HTMLElement;
			expect(volumesEl.textContent).toContain('Volumes: 0,0,0,0,0');
		});
	});

	describe('Component Props', () => {
		it('should accept bands prop', async () => {
			const { container } = render(AudioVisualizerTest, { bands: 3 });

			const bandsEl = container.querySelector('[data-testid="visualizer-bands"]') as HTMLElement;
			expect(bandsEl.textContent).toContain('Bands: 3');
		});

		it('should accept mode prop', async () => {
			const { container } = render(AudioVisualizerTest, { mode: 'radial' });

			const modeEl = container.querySelector('[data-testid="visualizer-mode"]') as HTMLElement;
			expect(modeEl.textContent).toContain('Mode: radial');
		});

		it('should accept onVolumesChange callback', async () => {
			const onVolumesChange = vi.fn();
			render(AudioVisualizerTest, { onVolumesChange });

			expect(onVolumesChange).toBeDefined();
		});
	});

	describe('Snippet Props', () => {
		it('should expose volumes in snippet', async () => {
			const { container } = render(AudioVisualizerTest);

			const volumesEl = container.querySelector(
				'[data-testid="visualizer-volumes"]'
			) as HTMLElement;
			expect(volumesEl).toBeTruthy();
		});

		it('should expose bands in snippet', async () => {
			const { container } = render(AudioVisualizerTest);

			const bandsEl = container.querySelector('[data-testid="visualizer-bands"]') as HTMLElement;
			expect(bandsEl).toBeTruthy();
		});

		it('should expose mode in snippet', async () => {
			const { container } = render(AudioVisualizerTest);

			const modeEl = container.querySelector('[data-testid="visualizer-mode"]') as HTMLElement;
			expect(modeEl).toBeTruthy();
		});

		it('should expose maxVolume in snippet', async () => {
			const { container } = render(AudioVisualizerTest);

			const maxVolEl = container.querySelector(
				'[data-testid="visualizer-max-volume"]'
			) as HTMLElement;
			expect(maxVolEl).toBeTruthy();
		});

		it('should expose avgVolume in snippet', async () => {
			const { container } = render(AudioVisualizerTest);

			const avgVolEl = container.querySelector(
				'[data-testid="visualizer-avg-volume"]'
			) as HTMLElement;
			expect(avgVolEl).toBeTruthy();
		});

		it('should expose isActive in snippet', async () => {
			const { container } = render(AudioVisualizerTest);

			const isActiveEl = container.querySelector(
				'[data-testid="visualizer-is-active"]'
			) as HTMLElement;
			expect(isActiveEl).toBeTruthy();
		});
	});
});
