import type {
  GatewayAppliedRuntimeSnapshot,
  GatewayNativeUpstreamStatus,
} from '@hpd/gateway-client';

export type GatewayDiscoveryCorrelation = 'Aligned' | 'Incomplete' | 'Mismatched';

export type GatewayDiscoveryProjection = Readonly<{
  upstreamId: string;
  state: GatewayNativeUpstreamStatus['discovery']['state'];
  profile: string | null;
  service: string | null;
  endpoint: string | null;
  membershipGeneration: string | null;
  membershipIdentity: string | null;
  appliedDestinationCount: number;
  effectiveDestinationCount: number | null;
  availableDestinationCount: number;
  nativeDestinationCount: number;
  eligibility: GatewayNativeUpstreamStatus['eligibility'];
  disposition: string | null;
  appliedAt: string | null;
  safeDiagnostic: string;
  correlation: GatewayDiscoveryCorrelation;
  reasons: readonly string[];
}>;

export type GatewayDiscoverySummary = Readonly<{
  total: number;
  discovered: number;
  fresh: number;
  degraded: number;
  unavailable: number;
  failed: number;
  indeterminate: number;
  mismatched: number;
}>;

export function projectGatewayDiscovery(
  native: readonly GatewayNativeUpstreamStatus[],
  applied: GatewayAppliedRuntimeSnapshot | undefined,
): readonly GatewayDiscoveryProjection[] {
  const nativeById = new Map(native.map(value => [value.upstreamId, value]));
  const appliedById = new Map((applied?.upstreams ?? []).map(value => [value.upstreamId, value]));
  const ids = [...new Set([...nativeById.keys(), ...appliedById.keys()])].sort(ordinal);
  return Object.freeze(ids.map(upstreamId => {
    const observed = nativeById.get(upstreamId);
    const effective = appliedById.get(upstreamId);
    const membershipIdentity = hashIdentity(observed?.discovery.membershipIdentity);
    const effectiveIdentity = hashIdentity(effective?.membershipIdentity);
    const correlation: GatewayDiscoveryCorrelation = !observed || !effective
      ? 'Incomplete'
      : membershipIdentity === null || effectiveIdentity === null
        ? 'Incomplete'
        : observed.discovery.appliedDestinationCount !== effective.destinationCount ||
        membershipIdentity !== effectiveIdentity ||
        observed.discovery.membershipGeneration !== effective.membershipGeneration
        ? 'Mismatched'
        : 'Aligned';
    return Object.freeze({
      upstreamId,
      state: observed?.discovery.state ?? 'NotObserved',
      profile: observed?.discovery.profile ?? effective?.discoveryProfile ?? null,
      service: observed?.discovery.service ?? effective?.service ?? null,
      endpoint: observed?.discovery.endpoint ?? effective?.endpoint ?? null,
      membershipGeneration: observed?.discovery.membershipGeneration ?? effective?.membershipGeneration ?? null,
      membershipIdentity,
      appliedDestinationCount: observed?.discovery.appliedDestinationCount ?? 0,
      effectiveDestinationCount: effective?.destinationCount ?? null,
      availableDestinationCount: observed?.availableDestinationCount ?? 0,
      nativeDestinationCount: observed?.allDestinationCount ?? 0,
      eligibility: observed?.eligibility ?? 'NotObserved',
      disposition: effective?.disposition ?? null,
      appliedAt: observed?.discovery.appliedAt ?? null,
      safeDiagnostic: observed?.discovery.safeDiagnostic ?? effective?.safeDiagnostic ?? 'Applied discovery truth was not observed.',
      correlation,
      reasons: Object.freeze((observed?.reasons ?? []).map(reason => reason.code)),
    });
  }));
}

function hashIdentity(value: { readonly algorithm?: string; readonly value?: string } | null | undefined): string | null {
  return value?.algorithm && value.value ? `${value.algorithm}:${value.value}` : null;
}

export function summarizeGatewayDiscovery(values: readonly GatewayDiscoveryProjection[]): GatewayDiscoverySummary {
  const summary = { total: values.length, discovered: 0, fresh: 0, degraded: 0, unavailable: 0, failed: 0, indeterminate: 0, mismatched: 0 };
  for (const value of values) {
    if (value.state !== 'NotRequired') summary.discovered++;
    if (value.state === 'AppliedFresh' || value.state === 'AppliedFreshEmpty') summary.fresh++;
    else if (value.state === 'AppliedLastKnownDegraded') summary.degraded++;
    else if (value.state === 'AppliedUnavailable') summary.unavailable++;
    else if (value.state === 'RefreshFailed') summary.failed++;
    else if (value.state === 'Resolving' || value.state === 'Indeterminate' || value.state === 'NotObserved') summary.indeterminate++;
    if (value.correlation === 'Mismatched') summary.mismatched++;
  }
  return Object.freeze(summary);
}

function ordinal(left: string, right: string): number {
  const leftScalars = Array.from(left, value => value.codePointAt(0)!);
  const rightScalars = Array.from(right, value => value.codePointAt(0)!);
  const length = Math.min(leftScalars.length, rightScalars.length);
  for (let index = 0; index < length; index++) {
    if (leftScalars[index]! < rightScalars[index]!) return -1;
    if (leftScalars[index]! > rightScalars[index]!) return 1;
  }
  return leftScalars.length - rightScalars.length;
}
