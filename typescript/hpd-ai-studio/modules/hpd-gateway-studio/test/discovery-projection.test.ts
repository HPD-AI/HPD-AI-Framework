import { describe, expect, it } from 'vitest';
import type { GatewayAppliedRuntimeSnapshot, GatewayNativeUpstreamStatus } from '@hpd/gateway-client';
import { projectGatewayDiscovery, summarizeGatewayDiscovery } from '../src/discovery-projection.ts';

describe('Gateway discovery projection', () => {
  it('correlates applied and native membership without exposing endpoint addresses', () => {
    const values = projectGatewayDiscovery([
      native('orders', 'AppliedFresh', '7', 'membership-a', 2, 2),
    ], applied([
      upstream('orders', 'fresh', '7', 'membership-a', 2),
    ]));

    expect(values).toEqual([expect.objectContaining({
      upstreamId: 'orders', state: 'AppliedFresh', profile: 'aspire', service: 'orders', endpoint: 'https',
      membershipGeneration: '7', membershipIdentity: 'sha-256:membership-a', appliedDestinationCount: 2,
      effectiveDestinationCount: 2, availableDestinationCount: 2, correlation: 'Aligned',
    })]);
    expect(JSON.stringify(values)).not.toContain('10.0.0.1');
  });

  it('keeps fresh-empty, degraded, unavailable, failed, indeterminate, and mismatch distinct', () => {
    const nativeValues = [
      native('empty', 'AppliedFreshEmpty', '1', 'empty', 0, 0),
      native('lkg', 'AppliedLastKnownDegraded', '2', 'lkg', 1, 1),
      native('unavailable', 'AppliedUnavailable', '3', 'unavailable', 0, 0),
      native('failed', 'RefreshFailed', '4', 'failed', 1, 1),
      native('unknown', 'Indeterminate', null, null, 0, 0),
      native('mismatch', 'AppliedFresh', '5', 'native', 2, 2),
    ];
    const effectiveValues = [
      upstream('empty', 'fresh', '1', 'empty', 0),
      upstream('lkg', 'lastKnownMembership', '2', 'lkg', 1),
      upstream('unavailable', 'unavailableWhenStale', '3', 'unavailable', 0),
      upstream('failed', 'refreshFailed', '4', 'failed', 1),
      upstream('mismatch', 'fresh', '5', 'effective', 2),
    ];

    const values = projectGatewayDiscovery(nativeValues, applied(effectiveValues));
    expect(values.map(value => value.state)).toEqual([
      'AppliedFreshEmpty', 'RefreshFailed', 'AppliedLastKnownDegraded', 'AppliedFresh', 'AppliedUnavailable', 'Indeterminate',
    ]);
    expect(values.find(value => value.upstreamId === 'mismatch')?.correlation).toBe('Mismatched');
    expect(summarizeGatewayDiscovery(values)).toEqual({
      total: 6, discovered: 6, fresh: 2, degraded: 1, unavailable: 1, failed: 1, indeterminate: 1, mismatched: 1,
    });
  });

  it('reports missing applied or native sides as incomplete and uses scalar-ordinal ordering', () => {
    const values = projectGatewayDiscovery([
      native('\u{10000}', 'NotObserved', null, null, 0, 0),
    ], applied([
      upstream('\uE000', 'fresh', '1', 'a', 1),
    ]));
    expect(values.map(value => value.upstreamId)).toEqual(['\uE000', '\u{10000}']);
    expect(values.every(value => value.correlation === 'Incomplete')).toBe(true);
  });
});

function native(
  upstreamId: string,
  state: GatewayNativeUpstreamStatus['discovery']['state'],
  generation: string | null,
  identity: string | null,
  appliedCount: number,
  availableCount: number,
): GatewayNativeUpstreamStatus {
  return {
    upstreamId, allDestinationCount: appliedCount, availableDestinationCount: availableCount,
    activeHealthyCount: 0, activeUnhealthyCount: 0, activeUnknownCount: appliedCount,
    passiveHealthyCount: 0, passiveUnhealthyCount: 0, passiveUnknownCount: appliedCount,
    eligibility: availableCount > 0 ? 'EligibleDestinationsPresent' : 'NoEligibleDestinations',
    availabilityPolicy: 'HealthyOrUnknown', countsTruncated: false, reasons: [], stamp: {
      authorityId: 'node', authorityKind: 'native', observationSequence: '1', observedAt: '2026-08-09T00:00:00Z',
      observedIdentity: identity, processInstanceId: 'process',
    },
    discovery: {
      state, profile: 'aspire', service: 'orders', endpoint: 'https', membershipGeneration: generation,
      membershipIdentity: identity === null ? null : { algorithm: 'sha-256', value: identity },
      appliedDestinationCount: appliedCount, appliedAt: '2026-08-09T00:00:00Z', safeDiagnostic: `state:${state}`,
    },
  };
}

function upstream(
  upstreamId: string,
  disposition: 'static' | 'fresh' | 'lastKnownMembership' | 'unavailableWhenStale' | 'refreshFailed',
  generation: string | null,
  identity: string,
  destinationCount: number,
) {
  return {
    upstreamId, kind: 'serviceDiscovery' as const, discoveryProfile: 'aspire', service: 'orders', endpoint: 'https',
    membershipGeneration: generation, membershipIdentity: { algorithm: 'sha-256', value: identity },
    destinationCount, disposition, safeDiagnostic: `disposition:${disposition}`,
  };
}

function applied(upstreams: ReturnType<typeof upstream>[]): GatewayAppliedRuntimeSnapshot {
  return {
    schemaVersion: 1, candidateId: 'candidate' as never, candidateContentHash: { algorithm: 'sha-256', value: 'candidate' },
    applicationId: '0123456789abcdef0123456789abcdef', symbolicPlanIdentity: { algorithm: 'sha-256', value: 'plan' },
    appliedAt: '2026-08-09T00:00:00Z', routes: [], upstreams, isComplete: true, isTruncated: false,
  };
}
