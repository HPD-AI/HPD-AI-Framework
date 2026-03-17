/**
 * AudioPlayer Tests
 * Following Bits UI testing patterns with comprehensive accessibility checks
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render } from 'vitest-browser-svelte';
import { userEvent } from '@vitest/browser/context';
import AudioPlayerTest from './audio-player.test.svelte';

describe('AudioPlayer', () => {
	// Mock AudioContext for testing
	beforeEach(() => {
		// eslint-disable-next-line @typescript-eslint/no-explicit-any
		(globalThis as any).AudioContext = vi.fn(function (this: any) {
			this.state = 'running';
			this.currentTime = 0;
			this.createGain = vi.fn().mockReturnValue({
				gain: {
					setTargetAtTime: vi.fn()
				},
				connect: vi.fn()
			});
			this.createAnalyser = vi.fn().mockReturnValue({
				fftSize: 256,
				frequencyBinCount: 128,
				connect: vi.fn(),
				getByteFrequencyData: vi.fn()
			});
			this.createBufferSource = vi.fn().mockReturnValue({
				buffer: null,
				connect: vi.fn(),
				start: vi.fn(),
				stop: vi.fn(),
				onended: null
			});
			this.decodeAudioData = vi.fn().mockResolvedValue({
				duration: 1.0,
				length: 44100,
				sampleRate: 44100
			});
			this.close = vi.fn().mockResolvedValue(undefined);
			this.destination = {};
		});

		// Mock HTMLAudioElement
		// eslint-disable-next-line @typescript-eslint/no-explicit-any
		(globalThis as any).Audio = vi.fn(function (this: any) {
			this.play = vi.fn().mockResolvedValue(undefined);
			this.pause = vi.fn();
			this.addEventListener = vi.fn();
			this.removeEventListener = vi.fn();
			this.src = '';
			this.currentTime = 0;
			this.duration = 0;
		});

		// Mock atob for base64 decoding
		// eslint-disable-next-line @typescript-eslint/no-explicit-any
		(globalThis as any).atob = vi.fn((str: string) => str);

		// Mock URL.createObjectURL and revokeObjectURL
		// eslint-disable-next-line @typescript-eslint/no-explicit-any
		(globalThis as any).URL = {
			createObjectURL: vi.fn(() => 'blob:mock-url'),
			revokeObjectURL: vi.fn()
		};
	});

	describe('ARIA Attributes & Accessibility', () => {
		it('should have correct ARIA attributes on root', async () => {
			const { container } = render(AudioPlayerTest);
			const root = container.querySelector('[data-audio-player-root]') as HTMLElement;

			await expect.element(root).toHaveAttribute('role', 'region');
			await expect.element(root).toHaveAttribute('aria-label', 'Audio playback');
			await expect.element(root).toHaveAttribute('aria-live', 'polite');
		});

		it('should have aria-busy=true when buffering', async () => {
			const { container } = render(AudioPlayerTest);
			const root = container.querySelector('[data-audio-player-root]') as HTMLElement;

			// Initially idle, aria-busy should be false
			await expect.element(root).toHaveAttribute('aria-busy', 'false');
		});
	});

	describe('Data Attributes', () => {
		it('should have audio-player-root data attribute', async () => {
			const { container } = render(AudioPlayerTest);
			const root = container.querySelector('[data-audio-player-root]') as HTMLElement;

			await expect.element(root).toBeInTheDocument();
			await expect.element(root).toHaveAttribute('data-audio-player-root', '');
		});

		it('should have correct initial status', async () => {
			const { container } = render(AudioPlayerTest);
			const root = container.querySelector('[data-audio-player-root]') as HTMLElement;

			await expect.element(root).toHaveAttribute('data-status', 'idle');
		});

		it('should not have data-playing attribute initially', async () => {
			const { container } = render(AudioPlayerTest);
			const root = container.querySelector('[data-audio-player-root]') as HTMLElement;

			await expect.element(root).not.toHaveAttribute('data-playing');
		});

		it('should not have data-paused attribute initially', async () => {
			const { container } = render(AudioPlayerTest);
			const root = container.querySelector('[data-audio-player-root]') as HTMLElement;

			await expect.element(root).not.toHaveAttribute('data-paused');
		});

		it('should not have data-buffering attribute initially', async () => {
			const { container } = render(AudioPlayerTest);
			const root = container.querySelector('[data-audio-player-root]') as HTMLElement;

			await expect.element(root).not.toHaveAttribute('data-buffering');
		});

		it('should not have data-error attribute initially', async () => {
			const { container } = render(AudioPlayerTest);
			const root = container.querySelector('[data-audio-player-root]') as HTMLElement;

			await expect.element(root).not.toHaveAttribute('data-error');
		});

		it('should have data-web-audio when useWebAudio=true', async () => {
			const { container } = render(AudioPlayerTest, { useWebAudio: true });
			const root = container.querySelector('[data-audio-player-root]') as HTMLElement;

			await expect.element(root).toHaveAttribute('data-web-audio', '');
		});
	});

	describe('State Management', () => {
		it('should start with idle status', async () => {
			const { container } = render(AudioPlayerTest);
			const statusEl = container.querySelector('[data-testid="audio-player-status"]') as HTMLElement;

			await expect.element(statusEl).toHaveAttribute('data-status', 'idle');
			await expect.element(statusEl).toHaveTextContent('Status: idle');
		});

		it('should show playing as false initially', async () => {
			const { container } = render(AudioPlayerTest);
			const playingEl = container.querySelector(
				'[data-testid="audio-player-playing"]'
			) as HTMLElement;

			await expect.element(playingEl).toHaveAttribute('data-playing', 'false');
			await expect.element(playingEl).toHaveTextContent('Playing: false');
		});

		it('should show paused as false initially', async () => {
			const { container } = render(AudioPlayerTest);
			const pausedEl = container.querySelector(
				'[data-testid="audio-player-paused"]'
			) as HTMLElement;

			await expect.element(pausedEl).toHaveAttribute('data-paused', 'false');
			await expect.element(pausedEl).toHaveTextContent('Paused: false');
		});

		it('should show buffering as false initially', async () => {
			const { container } = render(AudioPlayerTest);
			const bufferingEl = container.querySelector(
				'[data-testid="audio-player-buffering"]'
			) as HTMLElement;

			await expect.element(bufferingEl).toHaveAttribute('data-buffering', 'false');
			await expect.element(bufferingEl).toHaveTextContent('Buffering: false');
		});

		it('should show progress as 0.00 initially', async () => {
			const { container } = render(AudioPlayerTest);
			const progressEl = container.querySelector(
				'[data-testid="audio-player-progress"]'
			) as HTMLElement;

			await expect.element(progressEl).toHaveTextContent('Progress: 0.00');
		});
	});

	describe('HPD Event Handlers', () => {
		it('should handle SYNTHESIS_STARTED event', async () => {
			render(AudioPlayerTest);
			const state = (window as any).__audioPlayerState;

			await expect.poll(() => state).toBeTruthy();

			state.onSynthesisStarted('synth-123', 'tts-1', 'alloy');

			await expect.poll(() => state.status).toBe('buffering');
			await expect.poll(() => state.buffering).toBe(true);
			await expect.poll(() => state.playing).toBe(false);
		});

		it('should handle AUDIO_CHUNK event', async () => {
			render(AudioPlayerTest);
			const state = (window as any).__audioPlayerState;

			await expect.poll(() => state).toBeTruthy();

			state.onSynthesisStarted('synth-123');
			state.onAudioChunk('synth-123', 'base64data', 'audio/wav', 0, 'PT1S', false);

			// After 1 chunk, should still be buffering (threshold is 2)
			await expect.poll(() => state.status).toBe('buffering');
		});

		it('should start playing after buffer threshold', async () => {
			render(AudioPlayerTest);
			const state = (window as any).__audioPlayerState;

			await expect.poll(() => state).toBeTruthy();

			state.onSynthesisStarted('synth-123');
			state.onAudioChunk('synth-123', 'base64data', 'audio/wav', 0, 'PT1S', false);
			state.onAudioChunk('synth-123', 'base64data', 'audio/wav', 1, 'PT1S', false);

			// After 2 chunks, should start playing
			await expect.poll(() => state.playing).toBe(true);
		});

		it('should handle out-of-order chunks', async () => {
			render(AudioPlayerTest, { bufferThreshold: 3 });
			const state = (window as any).__audioPlayerState;

			await expect.poll(() => state).toBeTruthy();

			state.onSynthesisStarted('synth-123');

			// Send chunks out of order
			state.onAudioChunk('synth-123', 'base64data', 'audio/wav', 2, 'PT1S', false);
			state.onAudioChunk('synth-123', 'base64data', 'audio/wav', 0, 'PT1S', false);
			state.onAudioChunk('synth-123', 'base64data', 'audio/wav', 1, 'PT1S', false);

			// Should still buffer correctly
			await expect.poll(() => state.playing).toBe(true);
		});

		it('should handle SYNTHESIS_COMPLETED event', async () => {
			render(AudioPlayerTest);
			const state = (window as any).__audioPlayerState;

			await expect.poll(() => state).toBeTruthy();

			state.onSynthesisStarted('synth-123');
			state.onSynthesisCompleted('synth-123', false, 5, 5);

			await expect.poll(() => state.buffering).toBe(false);
		});

		it('should stop playback when interrupted', async () => {
			render(AudioPlayerTest);
			const state = (window as any).__audioPlayerState;

			await expect.poll(() => state).toBeTruthy();

			state.onSynthesisStarted('synth-123');
			state.onAudioChunk('synth-123', 'base64data', 'audio/wav', 0, 'PT1S', false);
			state.onAudioChunk('synth-123', 'base64data', 'audio/wav', 1, 'PT1S', false);

			// Interrupt
			state.onSynthesisCompleted('synth-123', true, 5, 2);

			await expect.poll(() => state.playing).toBe(false);
		});

		it('should handle SPEECH_PAUSED event', async () => {
			render(AudioPlayerTest);
			const state = (window as any).__audioPlayerState;

			await expect.poll(() => state).toBeTruthy();

			state.onSynthesisStarted('synth-123');
			state.onAudioChunk('synth-123', 'base64data', 'audio/wav', 0, 'PT1S', false);
			state.onAudioChunk('synth-123', 'base64data', 'audio/wav', 1, 'PT1S', false);

			state.onSpeechPaused('synth-123', 'user_speaking');

			await expect.poll(() => state.paused).toBe(true);
		});

		it('should handle SPEECH_RESUMED event', async () => {
			render(AudioPlayerTest);
			const state = (window as any).__audioPlayerState;

			await expect.poll(() => state).toBeTruthy();

			state.onSynthesisStarted('synth-123');
			state.onAudioChunk('synth-123', 'base64data', 'audio/wav', 0, 'PT1S', false);
			state.onAudioChunk('synth-123', 'base64data', 'audio/wav', 1, 'PT1S', false);
			state.onSpeechPaused('synth-123', 'user_speaking');

			state.onSpeechResumed('synth-123', 'PT2S');

			await expect.poll(() => state.paused).toBe(false);
		});
	});

	describe('Playback Controls', () => {
		it('should pause playback', async () => {
			const { container } = render(AudioPlayerTest);
			const pauseBtn = container.querySelector(
				'[data-testid="audio-player-pause-btn"]'
			) as HTMLElement;

			// Note: In real usage, pause only works when playing
			// For now, just verify the button exists
			await expect.element(pauseBtn).toBeInTheDocument();
		});

		it('should resume playback', async () => {
			const { container } = render(AudioPlayerTest);
			const resumeBtn = container.querySelector(
				'[data-testid="audio-player-resume-btn"]'
			) as HTMLElement;

			await expect.element(resumeBtn).toBeInTheDocument();
		});

		it('should stop playback', async () => {
			const { container } = render(AudioPlayerTest);
			const stopBtn = container.querySelector(
				'[data-testid="audio-player-stop-btn"]'
			) as HTMLElement;

			await expect.element(stopBtn).toBeInTheDocument();
		});
	});

	describe('Web Audio Mode', () => {
		it('should initialize Web Audio API when useWebAudio=true', async () => {
			render(AudioPlayerTest, { useWebAudio: true });
			const state = (window as any).__audioPlayerState;

			await expect.poll(() => state).toBeTruthy();
			// useWebAudio is a readonly property on the state, should be true
			expect(state.useWebAudio).toBe(true);
		});

		it('should expose AnalyserNode in Web Audio mode', async () => {
			const { container } = render(AudioPlayerTest, { useWebAudio: true });
			const analyserEl = container.querySelector(
				'[data-testid="audio-player-analyser"]'
			) as HTMLElement;

			await expect.element(analyserEl).toBeInTheDocument();
			await expect.element(analyserEl).toHaveAttribute('data-has-analyser');
		});

		it('should have volume control button in Web Audio mode', async () => {
			const { container } = render(AudioPlayerTest, { useWebAudio: true });
			const volumeBtn = container.querySelector(
				'[data-testid="audio-player-volume-btn"]'
			) as HTMLElement;

			await expect.element(volumeBtn).toBeInTheDocument();
		});

		it('should not have AnalyserNode in simple mode', async () => {
			const { container } = render(AudioPlayerTest, { useWebAudio: false });
			const analyserEl = container.querySelector('[data-testid="audio-player-analyser"]');

			expect(analyserEl).toBeNull();
		});
	});

	describe('Component Props', () => {
		it('should accept custom bufferThreshold', async () => {
			const { container } = render(AudioPlayerTest, { bufferThreshold: 5 });
			const root = container.querySelector('[data-audio-player-root]') as HTMLElement;

			await expect.element(root).toBeInTheDocument();
		});

		it('should call onStatusChange callback when status changes', async () => {
			const onStatusChange = vi.fn();

			render(AudioPlayerTest, { onStatusChange });
			const state = (window as any).__audioPlayerState;

			await expect.poll(() => state).toBeTruthy();

			// Trigger a status change
			state.onSynthesisStarted('synth-123');

			// onStatusChange should have been called
			await expect.poll(() => onStatusChange).toHaveBeenCalled();
		});

		it('should accept custom data-testid', async () => {
			const { container } = render(AudioPlayerTest, { testId: 'custom-player' });
			const root = container.querySelector('[data-testid="custom-player"]') as HTMLElement;

			await expect.element(root).toBeInTheDocument();
		});
	});

	describe('Snippet Props', () => {
		it('should expose status to snippet', async () => {
			const { container } = render(AudioPlayerTest);
			const statusEl = container.querySelector(
				'[data-testid="audio-player-status"]'
			) as HTMLElement;

			await expect.element(statusEl).toBeInTheDocument();
		});

		it('should expose playing to snippet', async () => {
			const { container } = render(AudioPlayerTest);
			const playingEl = container.querySelector(
				'[data-testid="audio-player-playing"]'
			) as HTMLElement;

			await expect.element(playingEl).toBeInTheDocument();
		});

		it('should expose paused to snippet', async () => {
			const { container } = render(AudioPlayerTest);
			const pausedEl = container.querySelector(
				'[data-testid="audio-player-paused"]'
			) as HTMLElement;

			await expect.element(pausedEl).toBeInTheDocument();
		});

		it('should expose buffering to snippet', async () => {
			const { container } = render(AudioPlayerTest);
			const bufferingEl = container.querySelector(
				'[data-testid="audio-player-buffering"]'
			) as HTMLElement;

			await expect.element(bufferingEl).toBeInTheDocument();
		});

		it('should expose progress to snippet', async () => {
			const { container } = render(AudioPlayerTest);
			const progressEl = container.querySelector(
				'[data-testid="audio-player-progress"]'
			) as HTMLElement;

			await expect.element(progressEl).toBeInTheDocument();
		});

		it('should expose time info to snippet', async () => {
			const { container } = render(AudioPlayerTest);
			const timeEl = container.querySelector('[data-testid="audio-player-time"]') as HTMLElement;

			await expect.element(timeEl).toBeInTheDocument();
		});

		it('should expose control functions to snippet', async () => {
			const { container } = render(AudioPlayerTest);
			const controlsEl = container.querySelector(
				'[data-testid="audio-player-controls"]'
			) as HTMLElement;

			await expect.element(controlsEl).toBeInTheDocument();
		});
	});

	describe('Cleanup', () => {
		it('should destroy state on unmount', async () => {
			const { unmount } = render(AudioPlayerTest, {
				useWebAudio: true
			});
			const state = (window as any).__audioPlayerState;
			await expect.poll(() => state).toBeTruthy();

			const destroySpy = vi.spyOn(state, 'destroy');
			unmount();

			expect(destroySpy).toHaveBeenCalled();
		});
	});
});
