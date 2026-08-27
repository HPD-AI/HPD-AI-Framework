import { test, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../src/browser-audio-transport-v1.js', import.meta.url), 'utf8');

async function install(page) {
  await page.addScriptTag({ type: 'module', content: `${source}\nglobalThis.BrowserAudioTransportV1 = BrowserAudioTransportV1;` });
}

test.beforeEach(async ({ page }) => {
  await page.goto('about:blank');
  await install(page);
});

test('virtual Web Audio capture and playout obey one generation fence', async ({ page }) => {
  const result = await page.evaluate(async () => {
    const authority = { sessionId: 'session-1', transportGeneration: 'transport-1' };
    const leaf = new globalThis.BrowserAudioTransportV1({ ...authority, capacity: 2 });
    const bind = leaf.bind(authority);
    const start = await leaf.start(authority);
    const samples = new Float32Array([0.25, -0.25, 0.5, -0.5]);
    const accepted = leaf.enqueuePlayout(authority, samples);
    samples[0] = 1;
    const rendered = leaf.renderNext(authority);
    const tracks = leaf.captureTrackCount;
    const stop = await leaf.stop(authority);
    return { bind, start, accepted, rendered, tracks, stop, state: leaf.state, virtual: leaf.isVirtualDevice };
  });

  expect(result.bind.kind).toBe('completed');
  expect(result.start.kind).toBe('completed');
  expect(result.accepted).toEqual({ kind: 'accepted', sequence: 1 });
  expect(result.rendered).toEqual({ kind: 'rendered', sequence: 1, sampleFrames: 4 });
  expect(result.tracks).toBe(1);
  expect(result.stop.kind).toBe('completed');
  expect(result.state).toBe('stopped');
  expect(result.virtual).toBe(true);
});

test('bounded queue refuses overflow and recovers after render', async ({ page }) => {
  const result = await page.evaluate(async () => {
    const authority = { sessionId: 'session-2', transportGeneration: 'transport-2' };
    const leaf = new globalThis.BrowserAudioTransportV1({ ...authority, capacity: 1 });
    leaf.bind(authority);
    await leaf.start(authority);
    const first = leaf.enqueuePlayout(authority, new Float32Array([0.1]));
    const overflow = leaf.enqueuePlayout(authority, new Float32Array([0.2]));
    leaf.renderNext(authority);
    const recovered = leaf.enqueuePlayout(authority, new Float32Array([0.3]));
    await leaf.stop(authority);
    return { first, overflow, recovered };
  });

  expect(result.first.kind).toBe('accepted');
  expect(result.overflow).toEqual({ kind: 'refused', safeCode: 'browser-transport-capacity-refused' });
  expect(result.recovered.kind).toBe('accepted');
});

test('stale generation and invalid lifecycle fail closed', async ({ page }) => {
  const result = await page.evaluate(async () => {
    const authority = { sessionId: 'session-3', transportGeneration: 'transport-3' };
    const leaf = new globalThis.BrowserAudioTransportV1({ ...authority });
    let stale;
    try { leaf.bind({ ...authority, transportGeneration: 'stale' }); }
    catch (error) { stale = error.message; }
    const beforeStart = leaf.enqueuePlayout(authority, new Float32Array([0.1]));
    leaf.bind(authority);
    const duplicateBind = leaf.bind(authority);
    return { stale, beforeStart, duplicateBind };
  });

  expect(result.stale).toBe('browser-transport-authority-stale');
  expect(result.beforeStart.safeCode).toBe('browser-transport-not-active');
  expect(result.duplicateBind.safeCode).toBe('browser-transport-transition-invalid');
});
