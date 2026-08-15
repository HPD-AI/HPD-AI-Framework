import { useCallback, useMemo, useState, useSyncExternalStore } from "react";
import type { BaseQueryOperation, BaseQuerySnapshot, BaseResult, BaseSubscription } from "@hpd/base-client";

export type BaseQueryState<T> =
  | { readonly kind: "loading" }
  | { readonly kind: "ready" | "refreshing"; readonly records: readonly T[]; readonly version: string }
  | { readonly kind: "stale"; readonly records: readonly T[]; readonly version: string }
  | { readonly kind: "error"; readonly terminal: boolean; readonly code: string }
  | { readonly kind: "disposed" };

export function useBaseQuery<T>(query: BaseQueryOperation<T>): BaseQueryState<T> {
  const store = useMemo(() => storeFor(query), [query]);
  return useSyncExternalStore(store.subscribe, store.getSnapshot, store.getServerSnapshot);
}

const queryStores = new WeakMap<object, QueryExternalStore<unknown>>();
function storeFor<T>(query: BaseQueryOperation<T>): QueryExternalStore<T> {
  let store = queryStores.get(query as object);
  if (store === undefined) { store = new QueryExternalStore(query as BaseQueryOperation<unknown>); queryStores.set(query as object, store); }
  return store as QueryExternalStore<T>;
}

export type BaseMutationState<T> = { readonly kind: "idle" } | { readonly kind: "pending" } | { readonly kind: "settled"; readonly result: BaseResult<T> };

export function useBaseMutation<TArguments extends readonly unknown[], TResult>(mutation: (...arguments_: TArguments) => Promise<BaseResult<TResult>>): {
  readonly state: BaseMutationState<TResult>;
  readonly mutate: (...arguments_: TArguments) => Promise<BaseResult<TResult>>;
} {
  const [state, setState] = useState<BaseMutationState<TResult>>({ kind: "idle" });
  const mutate = useCallback(async (...arguments_: TArguments): Promise<BaseResult<TResult>> => { setState({ kind: "pending" }); const result = await mutation(...arguments_); setState({ kind: "settled", result }); return result; }, [mutation]);
  return { state, mutate };
}

class QueryExternalStore<T> {
  readonly #query: BaseQueryOperation<T>;
  readonly #listeners = new Set<() => void>();
  #subscription: BaseSubscription | undefined;
  #releaseGeneration = 0;
  #state: BaseQueryState<T> = { kind: "loading" };
  public constructor(query: BaseQueryOperation<T>) { this.#query = query; }
  public readonly subscribe = (listener: () => void): (() => void) => {
    this.#listeners.add(listener);
    this.#releaseGeneration++;
    if (this.#subscription === undefined) this.#subscription = this.#query.watch(snapshot => this.update(snapshot));
    return () => {
      this.#listeners.delete(listener);
      if (this.#listeners.size === 0) { const generation = ++this.#releaseGeneration; queueMicrotask(() => { if (this.#listeners.size !== 0 || this.#releaseGeneration !== generation) return; this.#subscription?.close(); this.#subscription = undefined; }); }
    };
  };
  public readonly getSnapshot = (): BaseQueryState<T> => this.#state;
  public readonly getServerSnapshot = (): BaseQueryState<T> => ({ kind: "loading" });
  private update(snapshot: BaseQuerySnapshot<T>): void {
    this.#state = snapshot.stale
      ? { kind: "stale", records: snapshot.records, version: snapshot.version }
      : { kind: "ready", records: snapshot.records, version: snapshot.version };
    for (const listener of [...this.#listeners]) { try { listener(); } catch { /* observer isolation */ } }
  }
}
