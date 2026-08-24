import assert from 'node:assert/strict';
import test from 'node:test';
import { defineStudioRoute, formatStudioRoute, matchStudioRoute } from '../src/route.ts';

const route = defineStudioRoute({
  id: 'base.record.detail',
  segments: [
    { kind: 'literal', value: 'data' },
    { kind: 'literal', value: 'records' },
    { kind: 'parameter', name: 'record', codec: 'resource' }
  ],
  query: [
    { name: 'revision', codec: 'nonnegativeLong', required: false },
    { name: 'tab', codec: 'tab', allowed: ['summary', 'history'], required: false }
  ]
});

test('typed route format and match are canonical and immutable', () => {
  const url = formatStudioRoute(route, { record: 'cmVjb3JkLTE' }, { tab: 'history', revision: '12' });
  assert.equal(url, '/data/records/cmVjb3JkLTE?revision=12&tab=history');
  const match = matchStudioRoute(route, url);
  assert.deepEqual(match, {
    routeId: 'base.record.detail',
    parameters: { record: 'cmVjb3JkLTE' },
    query: { revision: '12', tab: 'history' },
    canonicalUrl: url
  });
  assert.equal(Object.isFrozen(match?.parameters), true);
});

test('unknown, duplicate, noncanonical, and malformed route members fail closed', () => {
  assert.equal(matchStudioRoute(route, '/data/records/cmVjb3JkLTE?unknown=x'), null);
  assert.equal(matchStudioRoute(route, '/data/records/cmVjb3JkLTE?tab=history&tab=history'), null);
  assert.equal(matchStudioRoute(route, '/data/records/%63mVjb3JkLTE'), null);
  assert.equal(matchStudioRoute(route, '/data/records/cmVjb3JkLTE?revision=01'), null);
  assert.throws(() => formatStudioRoute(route, { record: 'cmVjb3JkLTE', extra: 'x' }));
});

test('definition validation rejects duplicate names and open enum contracts', () => {
  assert.throws(() => defineStudioRoute({
    id: 'base.invalid',
    segments: [{ kind: 'parameter', name: 'id', codec: 'boundedId' }],
    query: [{ name: 'id', codec: 'boundedId', required: false }]
  }));
  assert.throws(() => defineStudioRoute({
    id: 'base.invalid',
    segments: [{ kind: 'parameter', name: 'kind', codec: 'enum' }],
    query: []
  }));
});
