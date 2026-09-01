export type StudioObservation<T> =
  | Readonly<{ readonly state: 'unobserved' }>
  | Readonly<{ readonly state: 'loading'; readonly previous: T | null; readonly previousObservedAt: string | null }>
  | Readonly<{ readonly state: 'value'; readonly value: T; readonly observedAt: string }>
  | Readonly<{ readonly state: 'stale'; readonly value: T; readonly observedAt: string; readonly code: string }>
  | Readonly<{ readonly state: 'denied'; readonly code: string }>
  | Readonly<{ readonly state: 'unavailable'; readonly code: string }>
  | Readonly<{ readonly state: 'unsupported'; readonly code: string }>
  | Readonly<{ readonly state: 'failed'; readonly code: string }>;

export type StudioReadResult<T> =
  | Readonly<{ readonly kind: 'value'; readonly value: T; readonly authority: StudioObservationAuthority }>
  | Readonly<{ readonly kind: 'denied'; readonly code: string }>
  | Readonly<{ readonly kind: 'unavailable'; readonly code: string }>
  | Readonly<{ readonly kind: 'unsupported'; readonly code: string }>
  | Readonly<{ readonly kind: 'failed'; readonly code: string }>;

export interface StudioRefreshController<T> {
  snapshot(): StudioObservation<T>;
  subscribe(listener: (observation: StudioObservation<T>) => void): () => void;
  refresh(signal?: AbortSignal): Promise<void>;
  invalidate(reason: StudioInvalidationReason): void;
  dispose(): void;
}

export interface StudioRefreshControllerOptions<T> {
  readonly read: (signal: AbortSignal) => Promise<StudioReadResult<T>>;
  readonly now?: () => Date;
}

export interface StudioObservationAuthority {
  readonly coherence: string;
  readonly authorizedThroughUtc: string;
}

export type StudioInvalidationReason = 'dataChanged' | 'gapUncertain' | 'principalChanged' |
  'policyChanged' | 'scopeChanged' | 'graphChanged' | 'authorizationExpired';

/**
 * Creates a single-flight, generation-fenced observation controller.
 *
 * A failed refresh retains an earlier authorized value only as explicit stale truth.
 * Invalidation aborts the active generation so late work cannot publish into a new
 * principal, route, or graph context.
 */
export function createStudioRefreshController<T>(
  options: StudioRefreshControllerOptions<T>
): StudioRefreshController<T> {
  if (!options || typeof options.read !== 'function') {
    throw new TypeError('Studio refresh requires a read operation.');
  }

  const now = options.now ?? (() => new Date());
  const listeners = new Set<(observation: StudioObservation<T>) => void>();
  let observation: StudioObservation<T> = unobserved();
  let generation = 0;
  let activeController: AbortController | null = null;
  let activeRefresh: Promise<void> | null = null;
  let disposed = false;
  let leaseTimer: ReturnType<typeof setTimeout> | null = null;

  const publish = (next: StudioObservation<T>): void => {
    observation = next;
    for (const listener of listeners) {
      try { listener(observation); } catch { /* observers cannot alter controller truth */ }
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
      activeRefresh = null;
      activeController = null;
      publish(unobserved());
    }, Math.min(delay, 2_147_483_647));
    (leaseTimer as unknown as { unref?: () => void }).unref?.();
    return true;
  };

  const currentValue = (): { readonly value: T; readonly observedAt: string } | null => {
    if (observation.state === 'value' || observation.state === 'stale') {
      return { value: observation.value, observedAt: observation.observedAt };
    }
    if (observation.state === 'loading' && observation.previous !== null) {
      return { value: observation.previous, observedAt: observation.previousObservedAt ?? now().toISOString() };
    }
    return null;
  };

  const refresh = (signal?: AbortSignal): Promise<void> => {
    if (disposed || signal?.aborted) return Promise.resolve();
    if (activeRefresh !== null) return settleOnAbort(activeRefresh, signal);

    const retained = currentValue();
    const capturedGeneration = ++generation;
    const controller = new AbortController();
    activeController = controller;
    publish(Object.freeze({
      state: 'loading',
      previous: retained?.value ?? null,
      previousObservedAt: retained?.observedAt ?? null
    }));

    const work = Promise.resolve()
      .then(() => options.read(controller.signal))
      .then((result) => {
        if (disposed || controller.signal.aborted || generation !== capturedGeneration) return;
        if (!result || typeof result !== 'object') {
          publish(failure(retained, 'studio.responseInvalid'));
          return;
        }
        switch (result.kind) {
          case 'value':
            if (!armLease(result.authority)) {
              publish(failure(retained, 'studio.authorizationExpired'));
              return;
            }
            publish(Object.freeze({ state: 'value', value: cloneAndFreeze(result.value), observedAt: now().toISOString() }));
            return;
          case 'denied':
          case 'unavailable':
          case 'unsupported':
            clearLease();
            publish(Object.freeze({ state: result.kind, code: validCode(result.code) }));
            return;
          case 'failed':
            publish(failure(retained, validCode(result.code)));
            return;
          default:
            publish(failure(retained, 'studio.responseInvalid'));
        }
      })
      .catch(() => {
        if (disposed || controller.signal.aborted || generation !== capturedGeneration) return;
        publish(failure(retained, 'studio.refreshFailed'));
      })
      .finally(() => {
        if (generation !== capturedGeneration) return;
        activeController = null;
        activeRefresh = null;
      });

    activeRefresh = work;
    return settleOnAbort(work, signal);
  };

  return Object.freeze({
    snapshot: () => observation,
    subscribe(listener: (next: StudioObservation<T>) => void) {
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
    refresh,
    invalidate(reason: StudioInvalidationReason) {
      if (disposed) return;
      generation++;
      activeController?.abort();
      activeController = null;
      activeRefresh = null;
      const prior = currentValue();
      const mayRetain = reason === 'dataChanged';
      if (!mayRetain) clearLease();
      publish(mayRetain && prior !== null
        ? Object.freeze({ state: 'stale', value: prior.value, observedAt: prior.observedAt, code: 'studio.dataChanged' })
        : unobserved());
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      generation++;
      activeController?.abort();
      activeController = null;
      activeRefresh = null;
      clearLease();
      listeners.clear();
      observation = unobserved();
    }
  });
}

function unobserved<T>(): StudioObservation<T> {
  return Object.freeze({ state: 'unobserved' });
}

function failure<T>(
  retained: { readonly value: T; readonly observedAt: string } | null,
  code: string
): StudioObservation<T> {
  return retained === null
    ? Object.freeze({ state: 'failed', code })
    : Object.freeze({ state: 'stale', value: retained.value, observedAt: retained.observedAt, code });
}

function validCode(value: unknown): string {
  return typeof value === 'string' && /^[a-z][a-zA-Z0-9.]{0,127}$/u.test(value)
    ? value
    : 'studio.responseInvalid';
}

function settleOnAbort(work: Promise<void>, signal?: AbortSignal): Promise<void> {
  if (signal === undefined) return work;
  if (signal.aborted) return Promise.resolve();
  return new Promise((resolve) => {
    const abort = (): void => resolve();
    signal.addEventListener('abort', abort, { once: true });
    void work.finally(() => {
      signal.removeEventListener('abort', abort);
      resolve();
    });
  });
}

function cloneAndFreeze<T>(value: T): T {
  return deepFreeze(structuredClone(value));
}

function deepFreeze<T>(value: T): T {
  if (value === null || typeof value !== 'object') return value;
  for (const child of Object.values(value)) deepFreeze(child);
  return Object.freeze(value);
}

function validIdentity(value: unknown): value is string {
  return typeof value === 'string' && value.length >= 1 && value.length <= 512;
}
