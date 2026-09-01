export type StudioCommandResult<TResult, TError, TResolution> =
  | Readonly<{ readonly kind: 'confirmed'; readonly result: TResult }>
  | Readonly<{ readonly kind: 'duplicate'; readonly result: TResult }>
  | Readonly<{ readonly kind: 'conflict'; readonly error: TError }>
  | Readonly<{ readonly kind: 'indeterminate'; readonly resolution: TResolution }>
  | Readonly<{ readonly kind: 'failed'; readonly error: TError }>;

export type StudioCommandWorkbenchState<TTarget, TInput, TPreview, TResult, TError, TResolution> =
  | Readonly<{ readonly kind: 'closed' }>
  | Readonly<{ readonly kind: 'draft'; readonly target: TTarget; readonly commandId: string; readonly input: TInput }>
  | Readonly<{ readonly kind: 'previewing'; readonly target: TTarget; readonly commandId: string; readonly input: TInput }>
  | Readonly<{ readonly kind: 'review'; readonly preview: TPreview }>
  | Readonly<{ readonly kind: 'executing'; readonly requestIdentity: string }>
  | Readonly<{ readonly kind: 'resolving'; readonly requestIdentity: string; readonly resolution: TResolution }>
  | Readonly<{ readonly kind: 'confirmed'; readonly result: TResult }>
  | Readonly<{ readonly kind: 'duplicate'; readonly result: TResult }>
  | Readonly<{ readonly kind: 'conflict'; readonly error: TError; readonly draft: Readonly<{ readonly target: TTarget; readonly commandId: string; readonly input: TInput }> }>
  | Readonly<{ readonly kind: 'indeterminate'; readonly resolution: TResolution }>
  | Readonly<{ readonly kind: 'unresolved'; readonly requestIdentity: string; readonly resolution: TResolution }>
  | Readonly<{ readonly kind: 'failed'; readonly error: TError }>;

export interface StudioIdentifiedCommandController<TTarget, TInput, TPreview, TResult, TError, TResolution> {
  snapshot(): StudioCommandWorkbenchState<TTarget, TInput, TPreview, TResult, TError, TResolution>;
  subscribe(listener: (state: StudioCommandWorkbenchState<TTarget, TInput, TPreview, TResult, TError, TResolution>) => void): () => void;
  open(target: TTarget, commandId: string, input: TInput): void;
  preview(signal?: AbortSignal): Promise<void>;
  execute(requestIdentity: string, signal?: AbortSignal): Promise<void>;
  resolve(signal?: AbortSignal): Promise<void>;
  close(): void;
  invalidate(): void;
  dispose(): void;
}

export interface StudioIdentifiedCommandControllerOptions<TTarget, TInput, TPreview, TResult, TError, TResolution> {
  readonly preview: (draft: Readonly<{ target: TTarget; commandId: string; input: TInput }>, signal: AbortSignal) => Promise<TPreview>;
  readonly execute: (request: Readonly<{ preview: TPreview; requestIdentity: string }>, signal: AbortSignal) => Promise<StudioCommandResult<TResult, TError, TResolution>>;
  readonly resolve: (resolution: TResolution, signal: AbortSignal) => Promise<StudioCommandResult<TResult, TError, TResolution>>;
  readonly failure: (error: unknown) => TError;
  readonly resolutionRequestIdentity: (resolution: TResolution) => string;
  readonly previewAuthority: (preview: TPreview) => Readonly<{ readonly coherence: string; readonly authorizedThroughUtc: string }>;
  readonly now?: () => Date;
}

/**
 * Owns one reviewed, identified command flow. It never retries a write and only
 * resolves an indeterminate outcome through the original resolution authority.
 */
export function createStudioIdentifiedCommandController<TTarget, TInput, TPreview, TResult, TError, TResolution>(
  options: StudioIdentifiedCommandControllerOptions<TTarget, TInput, TPreview, TResult, TError, TResolution>
): StudioIdentifiedCommandController<TTarget, TInput, TPreview, TResult, TError, TResolution> {
  if (!options || typeof options.preview !== 'function' || typeof options.execute !== 'function' ||
      typeof options.resolve !== 'function' || typeof options.failure !== 'function' ||
      typeof options.resolutionRequestIdentity !== 'function' || typeof options.previewAuthority !== 'function')
    throw new TypeError('Studio command options are invalid.');

  type State = StudioCommandWorkbenchState<TTarget, TInput, TPreview, TResult, TError, TResolution>;
  const listeners = new Set<(state: State) => void>();
  let state: State = Object.freeze({ kind: 'closed' });
  let generation = 0;
  let active: Promise<void> | null = null;
  let activeController: AbortController | null = null;
  let disposed = false;
  let draft: Readonly<{ readonly target: TTarget; readonly commandId: string; readonly input: TInput }> | null = null;
  let unresolved: Readonly<{ readonly requestIdentity: string; readonly resolution: TResolution }> | null = null;
  let previewTimer: ReturnType<typeof setTimeout> | null = null;
  const now = options.now ?? (() => new Date());

  const clearPreviewLease = (): void => { if (previewTimer !== null) clearTimeout(previewTimer); previewTimer = null; };
  const armPreviewLease = (preview: TPreview): boolean => {
    clearPreviewLease();
    const authority = options.previewAuthority(preview);
    const expiry = Date.parse(authority.authorizedThroughUtc);
    const delay = expiry - now().getTime();
    if (!validIdentity(authority.coherence) || !Number.isFinite(expiry) || delay <= 0) return false;
    previewTimer = setTimeout(() => {
      generation++;
      activeController?.abort();
      active = null;
      activeController = null;
      publish(draft === null ? Object.freeze({ kind: 'closed' }) : deepFreeze({ kind: 'draft', ...draft }) as State);
    }, Math.min(delay, 2_147_483_647));
    (previewTimer as unknown as { unref?: () => void }).unref?.();
    return true;
  };

  const publish = (next: State): void => {
    state = next;
    for (const listener of listeners) {
      try { listener(state); } catch { /* observers cannot alter controller truth */ }
    }
  };

  const run = (work: (signal: AbortSignal, capturedGeneration: number) => Promise<void>, signal?: AbortSignal): Promise<void> => {
    if (disposed || signal?.aborted) return Promise.resolve();
    if (active !== null) return settleOnAbort(active, signal);
    const controller = new AbortController();
    const capturedGeneration = generation;
    activeController = controller;
    const promise = Promise.resolve()
      .then(() => work(controller.signal, capturedGeneration))
      .finally(() => {
        if (generation !== capturedGeneration) return;
        active = null;
        activeController = null;
      });
    active = promise;
    return settleOnAbort(promise, signal);
  };

  const publishResult = (result: StudioCommandResult<TResult, TError, TResolution>, capturedGeneration: number): void => {
    if (disposed || generation !== capturedGeneration) return;
    switch (result.kind) {
      case 'confirmed':
      case 'duplicate':
        unresolved = null;
        draft = null;
        clearPreviewLease();
        publish(deepFreeze({ kind: result.kind, result: structuredClone(result.result) }) as State);
        return;
      case 'conflict':
        clearPreviewLease();
        if (draft === null) {
          publish(deepFreeze({ kind: 'failed', error: structuredClone(result.error) }) as State);
          return;
        }
        publish(deepFreeze({ kind: 'conflict', error: structuredClone(result.error), draft: structuredClone(draft) }) as State);
        return;
      case 'failed':
        clearPreviewLease();
        publish(deepFreeze({ kind: result.kind, error: structuredClone(result.error) }) as State);
        return;
      case 'indeterminate':
        clearPreviewLease();
        unresolved = deepFreeze({
          requestIdentity: options.resolutionRequestIdentity(result.resolution),
          resolution: structuredClone(result.resolution)
        });
        publish(deepFreeze({ kind: 'indeterminate', resolution: structuredClone(result.resolution) }) as State);
    }
  };

  return Object.freeze({
    snapshot: () => state,
    subscribe(listener: (next: State) => void) {
      if (disposed || typeof listener !== 'function') return () => {};
      listeners.add(listener);
      try { listener(state); } catch { /* observers cannot alter controller truth */ }
      return () => listeners.delete(listener);
    },
    open(target: TTarget, commandId: string, input: TInput) {
      if (disposed || !validIdentity(commandId)) throw new TypeError('Studio command identity is invalid.');
      if (state.kind === 'executing' || state.kind === 'resolving') {
        throw new Error('The identified command must reach an observable outcome before replacement.');
      }
      if (unresolved !== null) throw new Error('Resolve or invalidate the indeterminate command before opening another command.');
      generation++;
      activeController?.abort();
      active = null;
      activeController = null;
      clearPreviewLease();
      draft = deepFreeze({ target: structuredClone(target), commandId, input: structuredClone(input) });
      publish(deepFreeze({ kind: 'draft', ...draft }) as State);
    },
    preview(signal?: AbortSignal) {
      if (state.kind !== 'draft' || signal?.aborted) return Promise.resolve();
      const previewDraft = state;
      publish(deepFreeze({ ...previewDraft, kind: 'previewing' }) as State);
      return run(async (workSignal, capturedGeneration) => {
        try {
          const preview = await options.preview(Object.freeze({ target: previewDraft.target, commandId: previewDraft.commandId, input: previewDraft.input }), workSignal);
          if (!workSignal.aborted && !disposed && generation === capturedGeneration) {
            if (!armPreviewLease(preview)) {
              publish(deepFreeze({ ...previewDraft, kind: 'draft' }) as State);
              return;
            }
            publish(deepFreeze({ kind: 'review', preview: structuredClone(preview) }) as State);
          }
        } catch (error) {
          if (!workSignal.aborted && !disposed && generation === capturedGeneration) {
            publish(deepFreeze({ kind: 'failed', error: structuredClone(options.failure(error)) }) as State);
          }
        }
      }, signal);
    },
    execute(requestIdentity: string, signal?: AbortSignal) {
      if (state.kind !== 'review' || !validIdentity(requestIdentity) || signal?.aborted) return Promise.resolve();
      const preview = state.preview;
      // Once execution begins, server-side receipt resolution owns ambiguity. Preview
      // expiry must not abort or visually roll back a write that may have influenced state.
      clearPreviewLease();
      publish(Object.freeze({ kind: 'executing', requestIdentity }));
      return run(async (workSignal, capturedGeneration) => {
        try {
          const result = await options.execute(Object.freeze({ preview, requestIdentity }), workSignal);
          if (!workSignal.aborted) publishResult(result, capturedGeneration);
        } catch (error) {
          // The generated transport must return explicit indeterminate authority
          // when provider influence is possible; thrown local failures are safe failures.
          if (!workSignal.aborted && generation === capturedGeneration) {
            publish(deepFreeze({ kind: 'failed', error: structuredClone(options.failure(error)) }) as State);
          }
        }
      }, signal);
    },
    resolve(signal?: AbortSignal) {
      if ((state.kind !== 'indeterminate' && state.kind !== 'unresolved') || signal?.aborted) return Promise.resolve();
      const resolution = state.resolution;
      const requestIdentity = state.kind === 'unresolved' ? state.requestIdentity : options.resolutionRequestIdentity(resolution);
      if (!validIdentity(requestIdentity)) throw new TypeError('Studio resolution identity is invalid.');
      publish(deepFreeze({ kind: 'resolving', requestIdentity, resolution: structuredClone(resolution) }) as State);
      return run(async (workSignal, capturedGeneration) => {
        try {
          const result = await options.resolve(resolution, workSignal);
          if (!workSignal.aborted) publishResult(result, capturedGeneration);
        } catch {
          if (!workSignal.aborted && generation === capturedGeneration) {
            publish(deepFreeze({ kind: 'indeterminate', resolution: structuredClone(resolution) }) as State);
          }
        }
      }, signal);
    },
    close() {
      if (disposed) return;
      if (state.kind === 'executing' || state.kind === 'resolving') return;
      generation++;
      activeController?.abort();
      active = null;
      activeController = null;
      clearPreviewLease();
      publish(unresolved === null
        ? Object.freeze({ kind: 'closed' })
        : deepFreeze({ kind: 'unresolved', requestIdentity: unresolved.requestIdentity, resolution: structuredClone(unresolved.resolution) }) as State);
    },
    invalidate() {
      if (disposed) return;
      generation++;
      activeController?.abort();
      active = null;
      activeController = null;
      clearPreviewLease();
      unresolved = null;
      draft = null;
      publish(Object.freeze({ kind: 'closed' }));
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      generation++;
      activeController?.abort();
      active = null;
      activeController = null;
      clearPreviewLease();
      unresolved = null;
      draft = null;
      listeners.clear();
      state = Object.freeze({ kind: 'closed' });
    }
  });
}

function validIdentity(value: unknown): value is string {
  return typeof value === 'string' && value.length >= 1 && value.length <= 512;
}

function deepFreeze<T>(value: T): T {
  if (value === null || typeof value !== 'object') return value;
  for (const child of Object.values(value)) deepFreeze(child);
  return Object.freeze(value);
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
