import { toWireLiveQuery, type BaseQueryInput, type BaseQuerySnapshot, type BaseSubscription } from "./query.js";
import { parseBaseJson } from "./transport.js";

export interface BaseWebSocketLike {
  readonly readyState: number;
  onopen: ((event: Event) => void) | null;
  onmessage: ((event: MessageEvent<unknown>) => void) | null;
  onclose: ((event: CloseEvent) => void) | null;
  onerror: ((event: Event) => void) | null;
  send(data: string): void;
  close(code?: number, reason?: string): void;
}

export type BaseWebSocketFactory = (url: URL, accessToken: string | undefined) => BaseWebSocketLike | Promise<BaseWebSocketLike>;
export type BaseConnectivityState = { readonly kind: "offline" } | { readonly kind: "connecting"; readonly attempt: number } | { readonly kind: "online" } | { readonly kind: "degraded"; readonly reason: string } | { readonly kind: "closed" };

interface Welcome { readonly protocol: 2; readonly kind: "welcome"; readonly connectionId: string; readonly connectionEpoch: string; readonly heartbeatIntervalMs: number; readonly maxInboundBytes: number; readonly maxChannels: number; }
interface Joined { readonly protocol: 2; readonly kind: "joined"; readonly connectionId: string; readonly connectionEpoch: string; readonly ref: string; readonly channelEpoch: string; readonly delivery: string; }
interface Snapshot { readonly protocol: 2; readonly kind: "liveQuerySnapshot"; readonly connectionId: string; readonly connectionEpoch: string; readonly ref: string; readonly channelEpoch: string; readonly version: string; readonly source: "initial" | "rerun"; readonly value: unknown; }
interface ErrorMessage { readonly protocol: 2; readonly kind: "error"; readonly connectionId: string; readonly connectionEpoch: string; readonly ref?: string; readonly channelEpoch?: string; readonly terminal: boolean; readonly error: { readonly code: string }; }
interface RecordEventMessage { readonly protocol: 2; readonly kind: "liveRecordEvent" | "durableRecordEvent"; readonly connectionId: string; readonly connectionEpoch: string; readonly ref: string; readonly channelEpoch: string; readonly event: BaseRecordRealtimeEvent; readonly cursor?: string; }
type ServerMessage = Welcome | Joined | Snapshot | RecordEventMessage | ErrorMessage | { readonly protocol: 2; readonly kind: "heartbeatAck"; readonly connectionId: string; readonly connectionEpoch: string; readonly heartbeatId: string } | { readonly protocol: 2; readonly kind: "closed"; readonly connectionId: string; readonly connectionEpoch: string; readonly code: string; readonly retryable: boolean };

export interface BaseRecordRealtimeEvent { readonly eventId: string; readonly collectionId: string; readonly recordId: string; readonly operation: string; readonly occurredAt: string; readonly snapshot?: unknown; readonly before?: unknown; readonly invalidations?: readonly string[]; }
export interface BaseRecordFeedFilter { readonly recordId?: string; readonly operations?: readonly string[]; readonly eventTypes?: readonly string[]; readonly tenantId?: string; readonly includeSnapshots?: boolean; readonly includeBefore?: boolean; }
export type BaseRecordFeedRequest = { readonly kind: "live" | "durable"; readonly filter?: BaseRecordFeedFilter } | { readonly kind: "resume"; readonly cursor: string; readonly filter?: BaseRecordFeedFilter };
export interface BaseRecordFeedDelivery { readonly event: BaseRecordRealtimeEvent; readonly delivery: "live-at-most-once" | "durable-at-least-once"; readonly cursor?: string; }

interface SharedQuery {
  readonly key: string;
  readonly publicKey: string;
  readonly ref: string;
  readonly collectionId: string;
  readonly query: BaseQueryInput;
  readonly operation: object;
  readonly operationJson?: string;
  readonly resultTypeId: string;
  readonly decode: (value: unknown) => unknown;
  readonly observers: Set<(snapshot: BaseQuerySnapshot<unknown>) => void>;
  channelEpoch: string | undefined;
  lastVersion: bigint | undefined;
  last: BaseQuerySnapshot<unknown> | undefined;
  authoritative: readonly unknown[] | undefined;
  releaseGeneration: number;
}
interface OptimisticOverlay { readonly mutationId: string; readonly collectionId: string; readonly recordId: string; readonly kind: "create" | "patch" | "replace" | "delete" | "upsert"; readonly value: unknown; readonly expectedRevision?: string; readonly requestBytes: Uint8Array; readonly affectedQueryKeys: readonly string[]; readonly order: number; status: "pending" | "indeterminate"; }
interface SharedFeed { readonly ref: string; readonly collectionId: string; readonly request: BaseRecordFeedRequest; readonly observer: (delivery: BaseRecordFeedDelivery) => void | Promise<void>; readonly decodeRecord: (value: unknown) => unknown; channelEpoch: string | undefined; lastCursor: string | undefined; closed: boolean; }

export class BaseRealtimeManager {
  readonly #url: URL;
  readonly #factory: BaseWebSocketFactory;
  readonly #token: (() => string | undefined | Promise<string | undefined>) | undefined;
  readonly #identityChanged: (() => void) | undefined;
  readonly #queries = new Map<string, SharedQuery>();
  readonly #feeds = new Map<string, SharedFeed>();
  readonly #overlays = new Map<string, OptimisticOverlay>();
  #overlayOrder = 0;
  #socket: BaseWebSocketLike | undefined;
  #connectionId: string | undefined;
  #connectionEpoch: string | undefined;
  #attempt = 0;
  #closed = false;
  #reconnect: ReturnType<typeof setTimeout> | undefined;
  #heartbeat: ReturnType<typeof setTimeout> | undefined;
  #heartbeatDeadline: ReturnType<typeof setTimeout> | undefined;
  #heartbeatId: string | undefined;
  #pendingDispatch = 0;
  #maxInboundBytes = 1024 * 1024;
  #dispatch = Promise.resolve();
  #connectivity: BaseConnectivityState = { kind: "offline" };
  #activeToken: string | undefined;
  #hasToken = false;
  readonly #connectivityObservers = new Set<() => void>();

  public constructor(url: string, factory: BaseWebSocketFactory, token?: () => string | undefined | Promise<string | undefined>, identityChanged?: () => void) {
    const base = new URL(url, globalThis.location?.href ?? "http://localhost");
    base.protocol = base.protocol === "https:" ? "wss:" : "ws:";
    if (!base.pathname.endsWith("/")) base.pathname += "/";
    this.#url = new URL("realtime/v2/socket", base);
    this.#factory = factory;
    this.#token = token;
    this.#identityChanged = identityChanged;
  }

  public subscribe<T>(collectionId: string, query: BaseQueryInput, observer: (snapshot: BaseQuerySnapshot<T>) => void, decode: (value: unknown) => T): BaseSubscription {
    if (this.#closed) throw new Error("base.client.closed");
    const key = canonical({ collectionId, query });
    let shared = this.#queries.get(key);
    if (shared === undefined) {
      if (this.#queries.size >= 128) throw new Error("base.client.subscriptionLimit");
      shared = { key, publicKey: `q_${crypto.randomUUID().replaceAll("-", "")}`, ref: crypto.randomUUID(), collectionId, query: clone(query), operation: { kind: "collectionQuery", collectionId, query: toWireLiveQuery(query), take: query.take }, resultTypeId: `collection.${collectionId}.recordPage`, decode, observers: new Set(), channelEpoch: undefined, lastVersion: undefined, last: undefined, authoritative: undefined, releaseGeneration: 0 };
      this.#queries.set(key, shared);
    }
    const typedObserver = (snapshot: BaseQuerySnapshot<unknown>): void => observer(snapshot as BaseQuerySnapshot<T>);
    shared.releaseGeneration++;
    shared.observers.add(typedObserver);
    if (shared.last !== undefined) queueMicrotask(() => typedObserver(shared!.last!));
    void this.connect();
    let closed = false;
    return {
      get closed() { return closed; },
      close: () => {
        if (closed) return;
        closed = true;
        shared!.observers.delete(typedObserver);
        if (shared!.observers.size === 0) { const generation = ++shared!.releaseGeneration; queueMicrotask(() => { if (shared!.observers.size !== 0 || shared!.releaseGeneration !== generation) return; this.leave(shared!); this.#queries.delete(key); if (this.#queries.size === 0) this.disconnect(); }); }
      }
    };
  }

  public subscribeRead<T>(readId: string, parameters: unknown, observer: (snapshot: BaseQuerySnapshot<T>) => void, decode: (value: unknown) => T, encodedParameters?: string): BaseSubscription {
    const pseudoQuery: BaseQueryInput = { take: 500 }; const key = canonical({ readId, parameters }); let shared = this.#queries.get(key);
    if (shared === undefined) { shared = { key, publicKey: `q_${crypto.randomUUID().replaceAll("-", "")}`, ref: crypto.randomUUID(), collectionId: `read:${readId}`, query: pseudoQuery, operation: { kind: "registeredRead", readId, parameters: clone(parameters) }, ...(encodedParameters === undefined ? {} : { operationJson: `{\"kind\":\"registeredRead\",\"readId\":${JSON.stringify(readId)},\"parameters\":${encodedParameters}}` }), resultTypeId: `read.${readId}.rowPage`, decode, observers: new Set(), channelEpoch: undefined, lastVersion: undefined, last: undefined, authoritative: undefined, releaseGeneration: 0 }; this.#queries.set(key, shared); }
    const typed = (snapshot: BaseQuerySnapshot<unknown>): void => observer(snapshot as BaseQuerySnapshot<T>); shared.releaseGeneration++; shared.observers.add(typed); if (shared.last !== undefined) queueMicrotask(() => typed(shared!.last!)); void this.connect(); let closed = false;
    return { get closed() { return closed; }, close: () => { if (closed) return; closed = true; shared!.observers.delete(typed); if (shared!.observers.size === 0) { const generation = ++shared!.releaseGeneration; queueMicrotask(() => { if (shared!.observers.size !== 0 || shared!.releaseGeneration !== generation) return; this.leave(shared!); this.#queries.delete(key); if (this.#queries.size === 0) this.disconnect(); }); } } };
  }

  public subscribeFeed(collectionId: string, request: BaseRecordFeedRequest, observer: (delivery: BaseRecordFeedDelivery) => void | Promise<void>, decodeRecord: (value: unknown) => unknown): BaseSubscription {
    if (this.#closed) throw new Error("base.client.closed");
    if (this.#queries.size + this.#feeds.size >= 128) throw new Error("base.client.subscriptionLimit");
    if (request.kind === "resume" && request.cursor.length === 0) throw new TypeError("base.client.cursorInvalid");
    const ref = crypto.randomUUID(); const feed: SharedFeed = { ref, collectionId, request: clone(request), observer, decodeRecord, channelEpoch: undefined, lastCursor: undefined, closed: false };
    this.#feeds.set(ref, feed); void this.connect(); let closed = false;
    return { get closed() { return closed; }, close: () => { if (closed) return; closed = true; feed.closed = true; this.leaveFeed(feed); this.#feeds.delete(ref); if (this.#queries.size === 0 && this.#feeds.size === 0) this.disconnect(); } };
  }

  public readonly connectivity = {
    getSnapshot: (): BaseConnectivityState => this.#connectivity,
    subscribe: (observer: () => void): (() => void) => { this.#connectivityObservers.add(observer); return () => this.#connectivityObservers.delete(observer); }
  };

  public applyOptimistic(mutationId: string, collectionId: string, recordId: string, kind: OptimisticOverlay["kind"], value: unknown, expectedRevision: string | undefined, requestBytes: Uint8Array): void {
    if (this.#overlays.size >= 256) throw new Error("base.client.optimisticLimit");
    const affectedQueryKeys = [...this.#queries.values()].filter(query => query.collectionId === collectionId).map(query => query.key);
    this.#overlays.set(mutationId, { mutationId, collectionId, recordId, kind, value: clone(value), ...(expectedRevision === undefined ? {} : { expectedRevision }), requestBytes: requestBytes.slice(), affectedQueryKeys: Object.freeze(affectedQueryKeys), order: this.#overlayOrder++, status: "pending" });
    this.recompute(collectionId);
  }
  public markIndeterminate(mutationId: string): void { const overlay = this.#overlays.get(mutationId); if (overlay !== undefined) { overlay.status = "indeterminate"; this.setConnectivity({ kind: "degraded", reason: "base.runtime.batch.indeterminate" }); } }
  public reconcile(mutationId: string, collectionId: string, authoritative?: unknown): void {
    const overlay = this.#overlays.get(mutationId); if (overlay === undefined) return; this.#overlays.delete(mutationId);
    for (const query of this.#queries.values()) if (query.collectionId === collectionId && query.authoritative !== undefined) query.authoritative = overlay.kind === "delete"
      ? query.authoritative.filter(item => !isRecord(item) || item.id !== overlay.recordId)
      : authoritative === undefined ? query.authoritative : replaceRecord(query.authoritative, overlay.recordId, authoritative);
    this.recompute(collectionId);
  }
  public reject(mutationId: string, collectionId: string): void { if (this.#overlays.delete(mutationId)) this.recompute(collectionId); }

  public close(): void { this.#closed = true; this.#queries.clear(); this.#feeds.clear(); this.disconnect(); this.setConnectivity({ kind: "closed" }); }

  private async connect(): Promise<void> {
    if (this.#closed || this.#socket !== undefined || this.#queries.size + this.#feeds.size === 0) return;
    this.setConnectivity({ kind: "connecting", attempt: this.#attempt + 1 });
    try {
      const token = await this.#token?.();
      if (this.#hasToken && token !== this.#activeToken) this.resetIdentity();
      this.#activeToken = token; this.#hasToken = true;
      const socket = await this.#factory(this.#url, token);
      if (this.#closed) { socket.close(1000, "closed"); return; }
      this.#socket = socket;
      socket.onmessage = event => this.message(event.data);
      socket.onclose = () => this.closed();
      socket.onerror = () => socket.close();
    } catch { this.closed(); }
  }

  private message(payload: unknown): void {
    if (typeof payload !== "string" || new TextEncoder().encode(payload).length > this.#maxInboundBytes) { this.#socket?.close(1009, "base.realtime.payloadTooLarge"); return; }
    let value: unknown;
    try { value = parseBaseJson(payload); } catch { this.#socket?.close(1008, "base.realtime.protocol.invalid"); return; }
    if (!validServerMessage(value)) { this.#socket?.close(1008, "base.realtime.protocol.invalid"); return; }
    const message = value as ServerMessage;
    if (message.kind === "welcome") { if (this.#connectionEpoch !== undefined) this.#socket?.close(1008, "base.realtime.protocol.invalid"); else this.welcome(message); return; }
    if (this.#connectionEpoch === undefined) { this.#socket?.close(1008, "base.realtime.protocol.invalid"); return; }
    if (message.connectionId !== this.#connectionId || message.connectionEpoch !== this.#connectionEpoch) return;
    if (message.kind === "joined") {
      const query = this.byRef(message.ref);
      const feed = this.#feeds.get(message.ref);
      if ((query === undefined) === (feed === undefined) || !opaque(message.channelEpoch) || !["live-at-most-once", "durable-at-least-once", "live-query-snapshots"].includes(message.delivery)) { this.#socket?.close(1008, "base.realtime.protocol.invalid"); return; }
      if (query !== undefined) query.channelEpoch = message.channelEpoch;
      else feed!.channelEpoch = message.channelEpoch;
      return;
    }
    if (message.kind === "liveQuerySnapshot") {
      const query = this.byRef(message.ref); if (query === undefined) { this.#socket?.close(1008, "base.realtime.protocol.invalid"); return; }
      if (query.channelEpoch !== message.channelEpoch) return;
      this.snapshot(message);
    }
    else if (message.kind === "liveRecordEvent" || message.kind === "durableRecordEvent") {
      const feed = this.#feeds.get(message.ref); if (feed === undefined) { this.#socket?.close(1008, "base.realtime.protocol.invalid"); return; }
      if (feed.channelEpoch !== message.channelEpoch) return;
      if (++this.#pendingDispatch > 256) { this.#socket?.close(1008, "base.realtime.inboundOverflow"); return; }
      this.#dispatch = this.#dispatch.then(() => this.recordEvent(message)).finally(() => { this.#pendingDispatch--; });
    }
    else if (message.kind === "heartbeatAck" && message.heartbeatId === this.#heartbeatId) { this.#heartbeatId = undefined; if (this.#heartbeatDeadline !== undefined) clearTimeout(this.#heartbeatDeadline); this.#heartbeatDeadline = undefined; }
    else if (message.kind === "error" && message.terminal) {
      if (!isRecord(message.error) || typeof message.error.code !== "string" || message.error.code.length === 0 || message.error.code.length > 128) { this.#socket?.close(1008, "base.realtime.protocol.invalid"); return; }
      if (message.ref === undefined) { this.setConnectivity({ kind: "degraded", reason: message.error.code }); this.#socket?.close(1008, "terminal"); return; }
      const query = this.byRef(message.ref); const feed = this.#feeds.get(message.ref);
      if (query === undefined && feed === undefined) { this.#socket?.close(1008, "base.realtime.protocol.invalid"); return; }
      if (query !== undefined) this.#queries.delete(query.key); else { feed!.closed = true; this.#feeds.delete(message.ref); }
      this.setConnectivity({ kind: "degraded", reason: message.error.code });
    }
    else if (message.kind === "closed" && !message.retryable) this.close();
    else if (message.kind === "closed") this.#socket?.close(1012, "retry");
    else { this.#socket?.close(1008, "base.realtime.protocol.invalid"); }
  }

  private welcome(message: Welcome): void {
    if (!opaque(message.connectionId) || !opaque(message.connectionEpoch) || !Number.isInteger(message.heartbeatIntervalMs) || message.heartbeatIntervalMs < 1 || message.heartbeatIntervalMs > 60_000 || !Number.isInteger(message.maxChannels) || message.maxChannels < 1 || message.maxChannels > 128 || !Number.isInteger(message.maxInboundBytes) || message.maxInboundBytes < 256 || message.maxInboundBytes > 1024 * 1024) { this.#socket?.close(1008, "base.realtime.protocol.invalid"); return; }
    this.#connectionId = message.connectionId;
    this.#connectionEpoch = message.connectionEpoch;
    this.#maxInboundBytes = message.maxInboundBytes;
    this.#attempt = 0;
    this.setConnectivity({ kind: "online" });
    this.scheduleHeartbeat(message.heartbeatIntervalMs);
    for (const query of this.#queries.values()) {
      query.channelEpoch = undefined; query.lastVersion = undefined;
      if (query.last !== undefined) { query.last = { ...query.last, stale: true }; for (const observer of [...query.observers]) { try { observer(query.last); } catch { /* observer isolation */ } } }
      if (query.operationJson === undefined) this.send({ protocol: 2, kind: "join", connectionId: message.connectionId, connectionEpoch: message.connectionEpoch, ref: query.ref, channel: { kind: "liveQuery", operation: query.operation, resultTypeId: query.resultTypeId } });
      else this.sendRaw(`{\"protocol\":2,\"kind\":\"join\",\"connectionId\":${JSON.stringify(message.connectionId)},\"connectionEpoch\":${JSON.stringify(message.connectionEpoch)},\"ref\":${JSON.stringify(query.ref)},\"channel\":{\"kind\":\"liveQuery\",\"operation\":${query.operationJson},\"resultTypeId\":${JSON.stringify(query.resultTypeId)}}}`);
    }
    for (const feed of this.#feeds.values()) {
      feed.channelEpoch = undefined;
      const kind = feed.request.kind === "live" ? "live" : feed.lastCursor !== undefined || feed.request.kind === "resume" ? "resume" : "durable";
      const cursor = feed.lastCursor ?? (feed.request.kind === "resume" ? feed.request.cursor : undefined);
      this.send({ protocol: 2, kind: "join", connectionId: message.connectionId, connectionEpoch: message.connectionEpoch, ref: feed.ref, channel: { kind, collection: feed.collectionId, ...(cursor === undefined ? {} : { cursor }), filter: feed.request.filter ?? {} } });
    }
  }

  private async recordEvent(message: RecordEventMessage): Promise<void> {
    const feed = this.#feeds.get(message.ref);
    if (feed === undefined || feed.closed || feed.channelEpoch !== message.channelEpoch) return;
    if (message.kind === "durableRecordEvent" && (typeof message.cursor !== "string" || message.cursor.length === 0)) { this.#socket?.close(1008, "base.realtime.protocol.invalid"); return; }
    if (!validRecordEvent(message.event) || !opaque(message.ref) || !opaque(message.channelEpoch)) { this.#socket?.close(1008, "base.realtime.protocol.invalid"); return; }
    if (message.kind === "durableRecordEvent" && message.cursor === feed.lastCursor) return;
    let event: BaseRecordRealtimeEvent; try { event = { ...message.event, ...(message.event.snapshot === undefined ? {} : { snapshot: feed.decodeRecord(message.event.snapshot) }), ...(message.event.before === undefined ? {} : { before: feed.decodeRecord(message.event.before) }) }; } catch { this.#socket?.close(1008, "base.realtime.protocol.invalid"); return; }
    try {
      await feed.observer(Object.freeze({ event: clone(event), delivery: message.kind === "liveRecordEvent" ? "live-at-most-once" : "durable-at-least-once", ...(message.cursor === undefined ? {} : { cursor: message.cursor }) }));
      if (message.kind === "durableRecordEvent") feed.lastCursor = message.cursor!;
    } catch {
      feed.closed = true; this.leaveFeed(feed); this.#feeds.delete(feed.ref);
      this.setConnectivity({ kind: "degraded", reason: "base.client.observerFailed" });
    }
  }

  private snapshot(message: Snapshot): void {
    const query = this.byRef(message.ref);
    if (query === undefined || query.channelEpoch !== message.channelEpoch || !/^\d+$/u.test(message.version) || !validSnapshotPage(message.value)) { this.#socket?.close(1008, "base.realtime.protocol.invalid"); return; }
    const version = BigInt(message.version);
    if (query.lastVersion !== undefined && version <= query.lastVersion) return;
    query.lastVersion = version;
    const reconnect = query.authoritative !== undefined && message.source === "initial";
    try { query.authoritative = Object.freeze(message.value.items.map(item => query.decode(item))); } catch { this.#socket?.close(1008, "base.realtime.protocol.invalid"); return; }
    const snapshot: BaseQuerySnapshot<unknown> = { key: query.publicKey, connectionEpoch: message.connectionEpoch, channelEpoch: message.channelEpoch, records: Object.freeze(this.project(query)), source: reconnect ? "reconnected" : query.last === undefined ? "initial" : message.source, stale: false, version: message.version, receivedAt: Date.now() };
    query.last = snapshot;
    for (const observer of [...query.observers]) { try { observer(snapshot); } catch { /* observer isolation */ } }
  }

  private leave(query: SharedQuery): void {
    if (this.#connectionId === undefined || this.#connectionEpoch === undefined) return;
    this.send({ protocol: 2, kind: "leave", connectionId: this.#connectionId, connectionEpoch: this.#connectionEpoch, ref: query.ref });
  }
  private leaveFeed(feed: SharedFeed): void { if (this.#connectionId !== undefined && this.#connectionEpoch !== undefined) this.send({ protocol: 2, kind: "leave", connectionId: this.#connectionId, connectionEpoch: this.#connectionEpoch, ref: feed.ref }); }

  private send(value: unknown): void { if (this.#socket?.readyState === 1) this.#socket.send(JSON.stringify(value)); }
  private sendRaw(value: string): void { if (this.#socket?.readyState === 1) this.#socket.send(value); }
  private byRef(ref: string): SharedQuery | undefined { return [...this.#queries.values()].find(query => query.ref === ref); }
  private disconnect(): void { if (this.#reconnect !== undefined) clearTimeout(this.#reconnect); if (this.#heartbeat !== undefined) clearTimeout(this.#heartbeat); if (this.#heartbeatDeadline !== undefined) clearTimeout(this.#heartbeatDeadline); this.#reconnect = undefined; this.#heartbeat = undefined; this.#heartbeatDeadline = undefined; this.#socket?.close(1000, "closed"); this.#socket = undefined; this.#connectionId = undefined; this.#connectionEpoch = undefined; }
  private closed(): void {
    if (this.#heartbeat !== undefined) clearTimeout(this.#heartbeat); if (this.#heartbeatDeadline !== undefined) clearTimeout(this.#heartbeatDeadline); this.#heartbeat = undefined; this.#heartbeatDeadline = undefined; this.#heartbeatId = undefined;
    this.#socket = undefined; this.#connectionId = undefined; this.#connectionEpoch = undefined;
    if (this.#closed || this.#queries.size + this.#feeds.size === 0) return;
    this.setConnectivity({ kind: "offline" });
    const cap = Math.min(30_000, 250 * 2 ** Math.min(this.#attempt++, 7));
    this.#reconnect = setTimeout(() => { this.#reconnect = undefined; void this.connect(); }, Math.floor(Math.random() * cap));
  }
  private scheduleHeartbeat(interval: number): void {
    if (this.#heartbeat !== undefined) clearTimeout(this.#heartbeat);
    this.#heartbeat = setTimeout(() => {
      if (this.#connectionId === undefined || this.#connectionEpoch === undefined) return;
      if (this.#token !== undefined) void Promise.resolve(this.#token()).then(token => { if (token !== this.#activeToken) { this.resetIdentity(); this.#socket?.close(4001, "identity-changed"); } }).catch(() => this.#socket?.close(4001, "identity-refresh-failed"));
      const id = crypto.randomUUID(); this.#heartbeatId = id;
      this.send({ protocol: 2, kind: "heartbeat", connectionId: this.#connectionId, connectionEpoch: this.#connectionEpoch, heartbeatId: id });
      this.#heartbeatDeadline = setTimeout(() => this.#socket?.close(4000, "heartbeat-timeout"), interval);
      this.scheduleHeartbeat(interval);
    }, interval);
  }
  private resetIdentity(): void {
    this.#overlays.clear();
    this.#identityChanged?.();
    for (const query of this.#queries.values()) { query.authoritative = undefined; query.last = undefined; query.lastVersion = undefined; query.channelEpoch = undefined; }
  }
  private setConnectivity(state: BaseConnectivityState): void { this.#connectivity = state; for (const observer of [...this.#connectivityObservers]) { try { observer(); } catch { /* observer isolation */ } } }
  private project(query: SharedQuery): unknown[] {
    let records = [...(query.authoritative ?? [])];
    for (const overlay of [...this.#overlays.values()].filter(item => item.collectionId === query.collectionId).sort((a, b) => a.order - b.order)) records = applyOverlay(records, overlay);
    if (query.query.where !== undefined) records = records.filter(record => matchesRecord(record, query.query.where!));
    const orders = query.query.orderBy === undefined ? [] : Array.isArray(query.query.orderBy) ? query.query.orderBy : [query.query.orderBy];
    if (orders.length !== 0) records.sort((left, right) => { for (const order of orders) { const comparison = compareRecordField(left, right, order.field); if (comparison !== 0) return order.direction === "asc" ? comparison : -comparison; } return 0; });
    return records.slice(0, query.query.take);
  }
  private recompute(collectionId: string): void {
    for (const query of this.#queries.values()) if (query.collectionId === collectionId && query.last !== undefined) {
      query.last = { ...query.last, records: Object.freeze(this.project(query)) };
      for (const observer of [...query.observers]) { try { observer(query.last); } catch { /* observer isolation */ } }
    }
  }
}

function canonical(value: unknown): string { if (value === null || typeof value !== "object") return JSON.stringify(value); if (Array.isArray(value)) return `[${value.map(canonical).join(",")}]`; const record = value as Record<string, unknown>; return `{${Object.keys(record).sort().map(key => `${JSON.stringify(key)}:${canonical(record[key])}`).join(",")}}`; }
function clone<T>(value: T): T { return structuredClone(value); }
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === "object" && value !== null && !Array.isArray(value); }
function opaque(value: unknown): value is string { return typeof value === "string" && value.length >= 1 && value.length <= 128 && /^[\x21-\x7e]+$/u.test(value); }
function validRecordEvent(value: unknown): value is BaseRecordRealtimeEvent { return isRecord(value) && exact(value, ["eventId", "collectionId", "recordId", "operation", "occurredAt", ...(value.snapshot === undefined ? [] : ["snapshot"]), ...(value.before === undefined ? [] : ["before"]), ...(value.invalidations === undefined ? [] : ["invalidations"])]) && boundedString(value.eventId, 256) && boundedString(value.collectionId, 256) && boundedString(value.recordId, 256) && ["create", "patch", "replace", "delete", "upsert"].includes(value.operation as string) && boundedString(value.occurredAt, 64) && !Number.isNaN(Date.parse(value.occurredAt as string)) && (value.invalidations === undefined || Array.isArray(value.invalidations) && value.invalidations.length <= 256 && value.invalidations.every(item => boundedString(item, 4096))); }
function boundedString(value: unknown, bytes: number): value is string { return typeof value === "string" && value.length !== 0 && new TextEncoder().encode(value).length <= bytes && !/[\u0000-\u001f\u007f]/u.test(value); }
function replaceRecord(records: readonly unknown[], id: string, authoritative: unknown): readonly unknown[] { const next = records.filter(item => !isRecord(item) || item.id !== id); next.push(authoritative); return next; }
function applyOverlay(records: readonly unknown[], overlay: OptimisticOverlay): unknown[] {
  if (overlay.kind === "delete") return records.filter(item => !isRecord(item) || item.id !== overlay.recordId);
  const existing = records.find(item => isRecord(item) && item.id === overlay.recordId);
  if (existing === undefined) {
    if (overlay.kind !== "create" && overlay.kind !== "upsert") return [...records];
    const proposed = overlay.kind === "upsert" && isRecord(overlay.value) ? overlay.value.create : overlay.value;
    if (!isRecord(proposed)) return [...records];
    return [...records, Object.freeze({ collectionId: overlay.collectionId, id: overlay.recordId, payload: Object.freeze({ kind: "json", json: clone(proposed) }), metadata: Object.freeze({ ...(overlay.expectedRevision === undefined ? {} : { revision: overlay.expectedRevision }) }), policy: Object.freeze({ redacted: false, omittedFields: Object.freeze([]), readOnlyFields: Object.freeze([]) }) })];
  }
  const record = existing as Record<string, unknown>; const payload = isRecord(record.payload) ? record.payload : {}; const json = isRecord(payload.json) ? payload.json : {};
  const proposed = overlay.kind === "upsert" && isRecord(overlay.value) ? (overlay.value.update ?? overlay.value.create) : overlay.value;
  const visible = isRecord(proposed) ? Object.fromEntries(Object.entries(proposed).filter(([key]) => Object.hasOwn(json, key))) : {};
  const value = overlay.kind === "patch" || overlay.kind === "upsert" && isRecord(overlay.value) && overlay.value.updateMode === "patch" ? { ...json, ...visible } : visible;
  return records.map(item => item === existing ? { ...record, payload: { kind: "json", json: clone(value) } } : item);
}

function validServerMessage(value: unknown): value is ServerMessage {
  if (!isRecord(value) || value.protocol !== 2 || typeof value.kind !== "string") return false;
  const envelope = (extra: readonly string[]): boolean => exact(value, ["protocol", "kind", "connectionId", "connectionEpoch", ...extra]) && (value.kind === "welcome" || opaque(value.connectionId)) && (value.kind === "welcome" || opaque(value.connectionEpoch));
  if (value.kind === "welcome") return envelope(["heartbeatIntervalMs", "maxInboundBytes", "maxChannels"]) && opaque(value.connectionId) && opaque(value.connectionEpoch) && integerIn(value.heartbeatIntervalMs, 1, 60_000) && integerIn(value.maxInboundBytes, 256, 1024 * 1024) && integerIn(value.maxChannels, 1, 128);
  if (value.kind === "joined") return envelope(["ref", "channelEpoch", "delivery"]) && opaque(value.ref) && opaque(value.channelEpoch) && ["live-at-most-once", "durable-at-least-once", "live-query-snapshots"].includes(value.delivery as string);
  if (value.kind === "liveQuerySnapshot") return envelope(["ref", "channelEpoch", "version", "source", "value"]) && opaque(value.ref) && opaque(value.channelEpoch) && typeof value.version === "string" && /^\d+$/u.test(value.version) && (value.source === "initial" || value.source === "rerun");
  if (value.kind === "liveRecordEvent") return envelope(["ref", "channelEpoch", "event"]) && opaque(value.ref) && opaque(value.channelEpoch) && validRecordEvent(value.event);
  if (value.kind === "durableRecordEvent") return envelope(["ref", "channelEpoch", "event", "cursor"]) && opaque(value.ref) && opaque(value.channelEpoch) && validRecordEvent(value.event) && opaque(value.cursor);
  if (value.kind === "heartbeatAck") return envelope(["heartbeatId"]) && opaque(value.heartbeatId);
  if (value.kind === "error") return exact(value, ["protocol", "kind", "connectionId", "connectionEpoch", "terminal", "error", ...(value.ref === undefined ? [] : ["ref"]), ...(value.channelEpoch === undefined ? [] : ["channelEpoch"])]) && opaque(value.connectionId) && opaque(value.connectionEpoch) && typeof value.terminal === "boolean" && isRecord(value.error) && exact(value.error, ["code"]) && boundedString(value.error.code, 128) && (value.ref === undefined || opaque(value.ref)) && (value.channelEpoch === undefined || opaque(value.channelEpoch));
  if (value.kind === "closed") return envelope(["code", "retryable"]) && boundedString(value.code, 128) && typeof value.retryable === "boolean";
  return false;
}
function exact(value: Record<string, unknown>, keys: readonly string[]): boolean { return Object.keys(value).length === keys.length && Object.keys(value).every(key => keys.includes(key)); }
function integerIn(value: unknown, minimum: number, maximum: number): boolean { return Number.isInteger(value) && (value as number) >= minimum && (value as number) <= maximum; }
function validSnapshotPage(value: unknown): value is { readonly items: readonly unknown[]; readonly page: Record<string, unknown>; readonly count?: Record<string, unknown> } { if (!isRecord(value) || !exact(value, ["items", "page", ...(value.count === undefined ? [] : ["count"])]) || !Array.isArray(value.items) || !isRecord(value.page)) return false; const page = value.page; if (!exact(page, ["hasMore", ...["page", "perPage", "offset", "limit", "cursor", "nextCursor"].filter(key => page[key] !== undefined)]) || typeof page.hasMore !== "boolean") return false; for (const key of ["page", "perPage", "offset", "limit"]) if (page[key] !== undefined && (!Number.isSafeInteger(page[key]) || (page[key] as number) < 0)) return false; for (const key of ["cursor", "nextCursor"]) if (page[key] !== undefined && !boundedString(page[key], 4096)) return false; return value.count === undefined || isRecord(value.count) && exact(value.count, ["mode", "isExact", ...(value.count.total === undefined ? [] : ["total"])]) && ["none", "ifAvailable", "exact", "estimated", "limited"].includes(value.count.mode as string) && typeof value.count.isExact === "boolean" && (value.count.total === undefined || Number.isSafeInteger(value.count.total) && (value.count.total as number) >= 0); }

function matchesRecord(record: unknown, where: import("./query.js").BaseWhere): boolean {
  if (!isRecord(record) || !isRecord(record.payload)) return false;
  const source = isRecord(record.payload.json) ? record.payload.json : isRecord(record.payload.fields) ? record.payload.fields : {};
  if ("children" in where) return where.kind === "and" ? where.children.every(child => matchesRecord(record, child)) : where.kind === "or" ? where.children.some(child => matchesRecord(record, child)) : where.children.length === 1 && !matchesRecord(record, where.children[0]!);
  const present = Object.hasOwn(source, where.field); const actual = source[where.field];
  if (where.kind === "isDefined") return present;
  if (where.kind === "isNull") return present && actual === null;
  if ("values" in where) return present && where.values.some(value => compareValue(actual, value) === 0);
  if (!present) return false;
  if (!("operator" in where)) return false;
  const expected = where.value; if (expected === undefined) return false; const comparison = compareValue(actual, expected);
  return where.operator === "equal" ? comparison === 0 : where.operator === "notEqual" ? comparison !== 0 : where.operator === "lessThan" ? comparison < 0 : where.operator === "lessThanOrEqual" ? comparison <= 0 : where.operator === "greaterThan" ? comparison > 0 : where.operator === "greaterThanOrEqual" ? comparison >= 0 : typeof actual === "string" && typeof queryPrimitive(expected) === "string" ? stringOperator(actual, queryPrimitive(expected) as string, where.operator) : false;
}
function queryPrimitive(value: import("./query.js").BaseQueryValue): unknown { return value.kind === "null" ? null : value.kind === "string" ? value.string : value.kind === "boolean" ? value.boolean : value.kind === "integer" ? value.integer : value.kind === "number" ? value.number : value.kind === "dateTime" ? value.dateTime : value.id; }
function compareValue(actual: unknown, expected: import("./query.js").BaseQueryValue): number { const right = queryPrimitive(expected); if (actual === right) return 0; if ((typeof actual === "string" && typeof right === "string") || (typeof actual === "number" && typeof right === "number")) return actual < right ? -1 : 1; return Number.NaN; }
function stringOperator(actual: string, expected: string, operator: string): boolean { return operator === "contains" ? actual.includes(expected) : operator === "notContains" ? !actual.includes(expected) : operator === "startsWith" ? actual.startsWith(expected) : operator === "endsWith" ? actual.endsWith(expected) : operator === "like" ? actual.includes(expected.replaceAll("%", "")) : operator === "notLike" ? !actual.includes(expected.replaceAll("%", "")) : false; }
function compareRecordField(left: unknown, right: unknown, field: string): number { const value = (record: unknown): unknown => { if (!isRecord(record)) return undefined; if (field === "id") return record.id; if (!isRecord(record.payload)) return undefined; const source = isRecord(record.payload.json) ? record.payload.json : isRecord(record.payload.fields) ? record.payload.fields : {}; return source[field]; }; const a = value(left); const b = value(right); if (a === b) return 0; if (a === undefined) return 1; if (b === undefined) return -1; return (a as string | number) < (b as string | number) ? -1 : 1; }
