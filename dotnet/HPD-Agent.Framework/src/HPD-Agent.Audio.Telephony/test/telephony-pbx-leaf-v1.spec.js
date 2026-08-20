import test from 'node:test';
import assert from 'node:assert/strict';
import { TelephonyPbxLeafV1 } from '../src/telephony-pbx-leaf-v1.js';

const host = process.env.HPD_PBX_HOST ?? '127.0.0.1';
const advertisedHost = process.env.HPD_PBX_ADVERTISED_HOST ?? host;

test('real Asterisk PBX answers SIP and echoes PCMU RTP', async () => {
  const authority = { sessionId: 'call-session', transportGeneration: 'transport-1' };
  const leaf = new TelephonyPbxLeafV1(authority);
  assert.equal(leaf.bind(authority).kind, 'completed');
  const result = await leaf.callEcho(authority, { host, advertisedHost });
  assert.equal(result.kind, 'completed', result.detail);
  assert.equal(result.provider, 'asterisk');
  assert.equal(result.codec, 'PCMU/8000');
  assert.equal(result.sentPackets, 8);
  assert.ok(result.receivedPackets >= 3);
  assert.equal(leaf.state, 'stopped');
});

test('stale generation is rejected before signaling', () => {
  const authority = { sessionId: 'call-session', transportGeneration: 'transport-1' };
  const leaf = new TelephonyPbxLeafV1(authority);
  assert.throws(() => leaf.bind({ ...authority, transportGeneration: 'transport-2' }), /telephony-authority-stale/);
});

test('duplicate binding is refused without a second call owner', () => {
  const authority = { sessionId: 'call-session', transportGeneration: 'transport-1' };
  const leaf = new TelephonyPbxLeafV1(authority);
  assert.equal(leaf.bind(authority).kind, 'completed');
  assert.equal(leaf.bind(authority).safeCode, 'telephony-transition-invalid');
});
