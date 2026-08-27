import { test, expect } from '@playwright/test';
import { AccessToken } from 'livekit-server-sdk';
import { readFile } from 'node:fs/promises';

const bundle = await readFile(new URL('../dist/livekit-audio-transport-v1.js', import.meta.url), 'utf8');
const url = process.env.HPD_LIVEKIT_URL ?? 'ws://127.0.0.1:7880';
const httpOrigin = process.env.HPD_LIVEKIT_HTTP_ORIGIN ?? 'http://127.0.0.1:7880';

async function token(identity, room) {
  const access = new AccessToken('devkey', 'secret', { identity, ttl: '5m' });
  access.addGrant({ roomJoin: true, room, canPublish: true, canSubscribe: true });
  return await access.toJwt();
}

test.beforeEach(async ({ page }) => {
  await page.goto(httpOrigin);
  await page.addScriptTag({ content: bundle });
});

test('two real participants publish and subscribe audio through the SFU', async ({ page }, testInfo) => {
  const room = `hpd-audio-${testInfo.project.name}-${Date.now()}`;
  const publisherToken = await token('publisher', room);
  const subscriberToken = await token('subscriber', room);
  const result = await page.evaluate(async ({ url, publisherToken, subscriberToken }) => {
    const publisherAuthority = { sessionId: 'publisher-session', transportGeneration: 'transport-1' };
    const subscriberAuthority = { sessionId: 'subscriber-session', transportGeneration: 'transport-1' };
    const Publisher = globalThis.HpdLiveKit.LiveKitAudioTransportV1;
    const publisher = new Publisher(publisherAuthority);
    const subscriber = new Publisher(subscriberAuthority);
    publisher.bind(publisherAuthority);
    subscriber.bind(subscriberAuthority);
    await subscriber.connect(subscriberAuthority, url, subscriberToken);
    await publisher.connect(publisherAuthority, url, publisherToken);
    const published = await publisher.publishVirtualAudio(publisherAuthority);
    const received = await subscriber.waitForRemoteAudio(subscriberAuthority);
    const identities = [publisher.participantIdentity, subscriber.participantIdentity];
    await publisher.disconnect(publisherAuthority);
    await subscriber.disconnect(subscriberAuthority);
    return { published, received, identities, publisherState: publisher.state, subscriberState: subscriber.state };
  }, { url, publisherToken, subscriberToken });

  expect(result.published.kind).toBe('published');
  expect(result.received.kind).toBe('audio');
  expect(result.received.participantIdentity).toBe('publisher');
  expect(result.identities).toEqual(['publisher', 'subscriber']);
  expect(result.publisherState).toBe('stopped');
  expect(result.subscriberState).toBe('stopped');
});

test('generation mismatch cannot connect or publish', async ({ page }) => {
  const result = await page.evaluate(async () => {
    const authority = { sessionId: 'session', transportGeneration: 'transport-current' };
    const leaf = new globalThis.HpdLiveKit.LiveKitAudioTransportV1(authority);
    let stale;
    try { leaf.bind({ ...authority, transportGeneration: 'transport-stale' }); }
    catch (error) { stale = error.message; }
    const notActive = await leaf.publishVirtualAudio(authority);
    return { stale, notActive };
  });
  expect(result.stale).toBe('livekit-authority-stale');
  expect(result.notActive.safeCode).toBe('livekit-not-active');
});

test('changed lifecycle reuse is refused without a second room owner', async ({ page }) => {
  const result = await page.evaluate(() => {
    const authority = { sessionId: 'session', transportGeneration: 'transport-current' };
    const leaf = new globalThis.HpdLiveKit.LiveKitAudioTransportV1(authority);
    const first = leaf.bind(authority);
    const duplicate = leaf.bind(authority);
    return { first, duplicate, state: leaf.state };
  });
  expect(result.first.kind).toBe('completed');
  expect(result.duplicate.safeCode).toBe('livekit-transition-invalid');
  expect(result.state).toBe('bound');
});
