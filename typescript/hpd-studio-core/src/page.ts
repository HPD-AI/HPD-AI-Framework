import type { StudioInvalidationReason, StudioObservation, StudioObservationAuthority, StudioReadResult } from './observation.ts';

export interface StudioPageAccounting {
  readonly resultBytes: number;
  readonly transientBytes: number;
}

export interface StudioPage<TItem, TBoundary> {
  readonly items: readonly TItem[];
  readonly next: TBoundary | null;
  readonly pages: number;
  readonly observedAt: string;
  readonly authority: StudioObservationAuthority;
  readonly coverageChecksums: readonly string[];
  readonly accounting: StudioPageAccounting;
}

export interface StudioPageRequest<TBoundary> {
  readonly after: TBoundary | null;
  readonly take: number;
}

export interface StudioPageSegment<TItem, TBoundary> {
  readonly items: readonly TItem[];
  readonly next: TBoundary | null;
  readonly authority: StudioObservationAuthority;
  readonly coverageChecksum: string;
  readonly accounting: StudioPageAccounting;
}

export interface StudioPageController<TItem, TBoundary> {
  snapshot(): StudioObservation<StudioPage<TItem, TBoundary>>;
  subscribe(listener: (observation: StudioObservation<StudioPage<TItem, TBoundary>>) => void): () => void;
  loadInitial(signal?: AbortSignal): Promise<void>;
  loadMore(signal?: AbortSignal): Promise<void>;
  invalidate(reason: StudioInvalidationReason): void;
  dispose(): void;
}

export interface StudioPageControllerOptions<TItem, TBoundary> {
  readonly take: number;
  readonly maximumItems: number;
  readonly maximumPages: number;
  readonly maximumResultBytes: number;
  readonly maximumTransientBytes: number;
  readonly load: (
    request: StudioPageRequest<TBoundary>,
    signal: AbortSignal
  ) => Promise<StudioReadResult<StudioPageSegment<TItem, TBoundary>>>;
  readonly itemIdentity: (item: TItem) => string;
  readonly boundaryIdentity: (boundary: TBoundary) => string;
  readonly now?: () => Date;
}

/** Creates an explicitly bounded, append-only cursor-page controller. */
export function createStudioPageController<TItem, TBoundary>(
  options: StudioPageControllerOptions<TItem, TBoundary>
): StudioPageController<TItem, TBoundary> {
  validateOptions(options);
  const now = options.now ?? (() => new Date());
  const listeners = new Set<(observation: StudioObservation<StudioPage<TItem, TBoundary>>) => void>();
  let observation: StudioObservation<StudioPage<TItem, TBoundary>> = Object.freeze({ state: 'unobserved' });
  let generation = 0;
  let active: Promise<void> | null = null;
  let activeController: AbortController | null = null;
  let disposed = false;
  let leaseTimer: ReturnType<typeof setTimeout> | null = null;
  let seenBoundaries = new Set<string>();

  const publish = (next: StudioObservation<StudioPage<TItem, TBoundary>>): void => {
    observation = next;
    for (const listener of listeners) {
      try { listener(next); } catch { /* observers cannot alter controller truth */ }
    }
  };

  const clearLease = (): void => { if (leaseTimer !== null) clearTimeout(leaseTimer); leaseTimer = null; };
  const armLease = (authority: StudioObservationAuthority): boolean => {
    clearLease();
    const expiry = Date.parse(authority.authorizedThroughUtc);
    const delay = expiry - now().getTime();
    if (!validIdentity(authority.coherence) || !Number.isFinite(expiry) || delay <= 0) return false;
    leaseTimer = setTimeout(() => {
      generation++;
      activeController?.abort();
      active = null;
      publish(Object.freeze({ state: 'unobserved' }));
    }, Math.min(delay, 2_147_483_647));
    (leaseTimer as unknown as { unref?: () => void }).unref?.();
    return true;
  };

  const retained = (): StudioPage<TItem, TBoundary> | null =>
    observation.state === 'value' || observation.state === 'stale'
      ? observation.value
      : observation.state === 'loading'
        ? observation.previous
        : null;

  const execute = (reset: boolean, signal?: AbortSignal): Promise<void> => {
    if (disposed || signal?.aborted || active !== null) return active ?? Promise.resolve();
    if (!reset && observation.state !== 'value') return Promise.resolve();
    const lastGood = retained();
    const previous = reset ? null : lastGood;
    const candidateBoundaries = reset ? new Set<string>() : new Set(seenBoundaries);
    if (!reset && (previous === null || previous.next === null)) return Promise.resolve();
    if (!reset && previous !== null && previous.pages >= options.maximumPages) {
      publish(stale(previous, 'studio.page.maximumPages'));
      return Promise.resolve();
    }

    const controller = new AbortController();
    const capturedGeneration = ++generation;
    activeController = controller;
    publish(Object.freeze({
      state: 'loading',
      previous: lastGood,
      previousObservedAt: lastGood?.observedAt ?? null
    }));

    const request = Object.freeze({ after: previous?.next ?? null, take: options.take });
    const work = Promise.resolve()
      .then(() => options.load(request, controller.signal))
      .then((result) => {
        if (disposed || controller.signal.aborted || generation !== capturedGeneration) return;
        if (result.kind !== 'value') {
          publish(result.kind === 'failed'
            ? fail(lastGood, validCode(result.code))
            : Object.freeze({ state: result.kind, code: validCode(result.code) }));
          return;
        }
        let segment: StudioPageSegment<TItem, TBoundary>;
        try {
          segment = validateSegment(result.value, request, options);
        } catch {
          publish(fail(lastGood, 'studio.page.responseInvalid'));
          return;
        }
        if (previous !== null && (segment.authority.coherence !== previous.authority.coherence ||
            segment.authority.authorizedThroughUtc !== previous.authority.authorizedThroughUtc)) {
          publish(fail(lastGood, 'studio.page.authorityChanged'));
          return;
        }
        if (result.authority.coherence !== segment.authority.coherence ||
            result.authority.authorizedThroughUtc !== segment.authority.authorizedThroughUtc) {
          publish(fail(lastGood, 'studio.page.authorityMismatch'));
          return;
        }
        if (segment.next !== null) {
          const boundary = options.boundaryIdentity(segment.next);
          if (candidateBoundaries.has(boundary)) {
            publish(fail(lastGood, 'studio.page.repeatedBoundary'));
            return;
          }
          candidateBoundaries.add(boundary);
        }
        const priorItems = previous?.items ?? [];
        if (priorItems.length + segment.items.length > options.maximumItems) {
          publish(fail(lastGood, 'studio.page.maximumItems'));
          return;
        }
        const identities = new Set(priorItems.map(options.itemIdentity));
        for (const item of segment.items) {
          const identity = options.itemIdentity(item);
          if (!validIdentity(identity) || identities.has(identity)) {
            publish(fail(lastGood, 'studio.page.duplicateItem'));
            return;
          }
          identities.add(identity);
        }
        const resultBytes = (previous?.accounting.resultBytes ?? 0) + segment.accounting.resultBytes;
        const transientBytes = (previous?.accounting.transientBytes ?? 0) + segment.accounting.transientBytes;
        if (!Number.isSafeInteger(resultBytes) || !Number.isSafeInteger(transientBytes) ||
            resultBytes > options.maximumResultBytes || transientBytes > options.maximumTransientBytes) {
          publish(fail(lastGood, 'studio.page.accountingExceeded'));
          return;
        }
        const value = deepFreeze({
          items: [...priorItems, ...segment.items.map((item) => structuredClone(item))],
          next: segment.next === null ? null : structuredClone(segment.next),
          pages: (previous?.pages ?? 0) + 1,
          observedAt: now().toISOString(),
          authority: structuredClone(segment.authority),
          coverageChecksums: [...(previous?.coverageChecksums ?? []), segment.coverageChecksum],
          accounting: {
            resultBytes,
            transientBytes
          }
        }) as StudioPage<TItem, TBoundary>;
        if (!armLease(value.authority)) {
          publish(Object.freeze({ state: 'unobserved' }));
          return;
        }
        seenBoundaries = candidateBoundaries;
        publish(Object.freeze({ state: 'value', value, observedAt: value.observedAt }));
      })
      .catch(() => {
        if (disposed || controller.signal.aborted || generation !== capturedGeneration) return;
        publish(fail(lastGood, 'studio.page.loadFailed'));
      })
      .finally(() => {
        if (generation !== capturedGeneration) return;
        activeController = null;
        active = null;
      });
    active = work;
    return work;
  };

  return Object.freeze({
    snapshot: () => observation,
    subscribe(listener: (next: StudioObservation<StudioPage<TItem, TBoundary>>) => void) {
      if (disposed || typeof listener !== 'function') return () => {};
      listeners.add(listener);
      try { listener(observation); } catch { /* observers cannot alter controller truth */ }
      let subscribed = true;
      return () => {
        if (!subscribed) return;
        subscribed = false;
        listeners.delete(listener);
      };
    },
    loadInitial: (signal?: AbortSignal) => execute(true, signal),
    loadMore: (signal?: AbortSignal) => execute(false, signal),
    invalidate(reason: StudioInvalidationReason) {
      if (disposed) return;
      generation++;
      activeController?.abort();
      activeController = null;
      active = null;
      const previous = retained();
      if (reason !== 'dataChanged') clearLease();
      if (reason !== 'dataChanged') seenBoundaries = new Set<string>();
      publish(reason === 'dataChanged' && previous !== null
        ? stale(previous, 'studio.dataChanged')
        : Object.freeze({ state: 'unobserved' }));
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      generation++;
      activeController?.abort();
      activeController = null;
      active = null;
      clearLease();
      listeners.clear();
      observation = Object.freeze({ state: 'unobserved' });
    }
  });
}

function validateOptions<TItem, TBoundary>(options: StudioPageControllerOptions<TItem, TBoundary>): void {
  if (!options || !Number.isInteger(options.take) || options.take < 1 || options.take > 256 ||
      !Number.isInteger(options.maximumItems) || options.maximumItems < options.take || options.maximumItems > 10_000 ||
      !Number.isInteger(options.maximumPages) || options.maximumPages < 1 || options.maximumPages > 256 ||
      !Number.isSafeInteger(options.maximumResultBytes) || options.maximumResultBytes < 1 ||
      !Number.isSafeInteger(options.maximumTransientBytes) || options.maximumTransientBytes < 1 ||
      typeof options.load !== 'function' || typeof options.itemIdentity !== 'function' ||
      typeof options.boundaryIdentity !== 'function') {
    throw new TypeError('Studio page options are invalid.');
  }
}

function validateSegment<TItem, TBoundary>(
  value: StudioPageSegment<TItem, TBoundary>,
  _request: StudioPageRequest<TBoundary>,
  options: StudioPageControllerOptions<TItem, TBoundary>
): StudioPageSegment<TItem, TBoundary> {
  if (!value || typeof value !== 'object' || !Array.isArray(value.items) || value.items.length > options.take ||
      (value.next !== null && value.items.length === 0) || !validIdentity(value.coverageChecksum) ||
      !value.authority || !validIdentity(value.authority.coherence) ||
      !validAccounting(value.accounting)) {
    throw new TypeError('Studio page segment is invalid.');
  }
  if (value.next !== null) {
    const next = options.boundaryIdentity(value.next);
    if (!validIdentity(next)) {
      throw new TypeError('Studio page boundary is invalid.');
    }
  }
  return value;
}

function validAccounting(value: StudioPageAccounting): boolean {
  return value !== null && typeof value === 'object' &&
    Number.isSafeInteger(value.resultBytes) && value.resultBytes >= 0 &&
    Number.isSafeInteger(value.transientBytes) && value.transientBytes >= 0;
}

function fail<TItem, TBoundary>(
  previous: StudioPage<TItem, TBoundary> | null,
  code: string
): StudioObservation<StudioPage<TItem, TBoundary>> {
  return previous === null
    ? Object.freeze({ state: 'failed', code })
    : stale(previous, code);
}

function stale<TItem, TBoundary>(
  previous: StudioPage<TItem, TBoundary>,
  code: string
): StudioObservation<StudioPage<TItem, TBoundary>> {
  return Object.freeze({ state: 'stale', value: previous, observedAt: previous.observedAt, code });
}

function validIdentity(value: unknown): value is string {
  return typeof value === 'string' && value.length >= 1 && value.length <= 512;
}

function validCode(value: unknown): string {
  return typeof value === 'string' && /^[a-z][a-zA-Z0-9.]{0,127}$/u.test(value)
    ? value
    : 'studio.responseInvalid';
}

function deepFreeze<T>(value: T): T {
  if (value === null || typeof value !== 'object' || Object.isFrozen(value)) return value;
  for (const child of Object.values(value)) deepFreeze(child);
  return Object.freeze(value);
}
