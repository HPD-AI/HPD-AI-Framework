export type StudioActivityState =
  | Readonly<{ readonly kind: 'quiet' }>
  | Readonly<{ readonly kind: 'updatesAvailable'; readonly count: 'one' | 'several' | 'many' }>
  | Readonly<{ readonly kind: 'pausedForActivity' }>
  | Readonly<{ readonly kind: 'refreshing' }>;

export interface StudioActivityController {
  snapshot(): StudioActivityState;
  subscribe(listener: (state: StudioActivityState) => void): () => void;
  observeHint(key: string): void;
  refresh(signal?: AbortSignal): Promise<void>;
  invalidate(): void;
  dispose(): void;
}

export interface StudioActivityControllerOptions {
  readonly refresh: (signal: AbortSignal) => Promise<void>;
  readonly policy: Readonly<{
    readonly kind: 'explicitRefreshOnly' | 'governedInvalidationRefresh';
    readonly maximumHintsPerRollingSecond: number;
    readonly maximumSupersededRefreshes: number;
    readonly maximumCoalescedKeys: number;
  }>;
  readonly now?: () => number;
}

/** Coalesces non-authoritative hints and refreshes only through finite truth. */
export function createStudioActivityController(options: StudioActivityControllerOptions): StudioActivityController {
  if (!options || typeof options.refresh !== 'function' ||
      !options.policy || !['explicitRefreshOnly', 'governedInvalidationRefresh'].includes(options.policy.kind) ||
      !Number.isInteger(options.policy.maximumHintsPerRollingSecond) || options.policy.maximumHintsPerRollingSecond < 1 || options.policy.maximumHintsPerRollingSecond > 1_000 ||
      !Number.isInteger(options.policy.maximumSupersededRefreshes) || options.policy.maximumSupersededRefreshes < 1 || options.policy.maximumSupersededRefreshes > 100 ||
      !Number.isInteger(options.policy.maximumCoalescedKeys) || options.policy.maximumCoalescedKeys < 1 || options.policy.maximumCoalescedKeys > 2_048) {
    throw new TypeError('Studio activity options are invalid.');
  }
  const now = options.now ?? Date.now;
  const listeners = new Set<(state: StudioActivityState) => void>();
  let state: StudioActivityState = Object.freeze({ kind: 'quiet' });
  let hintTimes: number[] = [];
  let pendingKeys = new Set<string>();
  let supersededRefreshes = 0;
  let generation = 0;
  let active: Promise<void> | null = null;
  let activeController: AbortController | null = null;
  let disposed = false;

  const publish = (next: StudioActivityState): void => {
    state = next;
    for (const listener of listeners) {
      try { listener(state); } catch { /* observers cannot alter activity truth */ }
    }
  };
  const count = (value: number): 'one' | 'several' | 'many' => value === 1 ? 'one' : value <= 9 ? 'several' : 'many';

  return Object.freeze({
    snapshot: () => state,
    subscribe(listener: (next: StudioActivityState) => void) {
      if (disposed || typeof listener !== 'function') return () => {};
      listeners.add(listener);
      try { listener(state); } catch { /* observers cannot alter activity truth */ }
      return () => listeners.delete(listener);
    },
    observeHint(key: string) {
      if (disposed || options.policy.kind === 'explicitRefreshOnly' || state.kind === 'pausedForActivity') return;
      if (!/^[A-Za-z0-9][A-Za-z0-9._:-]{0,255}$/u.test(key)) throw new TypeError('Studio invalidation key is invalid.');
      const observedAt = now();
      if (!Number.isFinite(observedAt)) return;
      hintTimes = hintTimes.filter((value) => observedAt >= value && observedAt - value < 1_000);
      if (hintTimes.length >= options.policy.maximumHintsPerRollingSecond ||
          (!pendingKeys.has(key) && pendingKeys.size >= options.policy.maximumCoalescedKeys)) {
        publish(Object.freeze({ kind: 'pausedForActivity' }));
        return;
      }
      hintTimes.push(observedAt);
      pendingKeys.add(key);
      if (active === null) publish(Object.freeze({ kind: 'updatesAvailable', count: count(pendingKeys.size) }));
    },
    refresh(signal?: AbortSignal) {
      if (disposed || signal?.aborted) return Promise.resolve();
      if (active !== null) return settleOnAbort(active, signal);
      const controller = new AbortController();
      const capturedGeneration = generation;
      activeController = controller;
      const capturedKeys = pendingKeys;
      pendingKeys = new Set<string>();
      publish(Object.freeze({ kind: 'refreshing' }));
      const work = Promise.resolve()
        .then(() => options.refresh(controller.signal))
        .then(() => {
          if (disposed || controller.signal.aborted || generation !== capturedGeneration) return;
          if (pendingKeys.size > 0) supersededRefreshes++;
          else supersededRefreshes = 0;
          if (supersededRefreshes >= options.policy.maximumSupersededRefreshes) {
            publish(Object.freeze({ kind: 'pausedForActivity' }));
          } else publish(pendingKeys.size === 0
            ? Object.freeze({ kind: 'quiet' })
            : Object.freeze({ kind: 'updatesAvailable', count: count(pendingKeys.size) }));
        })
        .catch(() => {
          if (!disposed && !controller.signal.aborted && generation === capturedGeneration) {
            let overflow = false;
            for (const key of capturedKeys) {
              if (!pendingKeys.has(key) && pendingKeys.size >= options.policy.maximumCoalescedKeys) { overflow = true; break; }
              pendingKeys.add(key);
            }
            publish(overflow
              ? Object.freeze({ kind: 'pausedForActivity' })
              : Object.freeze({ kind: 'updatesAvailable', count: count(Math.max(1, pendingKeys.size)) }));
          }
        })
        .finally(() => {
          if (generation !== capturedGeneration) return;
          active = null;
          activeController = null;
        });
      active = work;
      return settleOnAbort(work, signal);
    },
    invalidate() {
      if (disposed) return;
      generation++;
      activeController?.abort();
      activeController = null;
      active = null;
      hintTimes = [];
      pendingKeys = new Set<string>();
      supersededRefreshes = 0;
      publish(Object.freeze({ kind: 'quiet' }));
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      generation++;
      activeController?.abort();
      activeController = null;
      active = null;
      listeners.clear();
      state = Object.freeze({ kind: 'quiet' });
    }
  });
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
