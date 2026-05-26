export type ShellLayoutSnapshot = {
  sidebarCollapsed: boolean;
  expandedAppPaneWidth: number | null;
  collapsedAppPaneWidth: number | null;
};

export type ShellLayoutStorage = {
  load(): ShellLayoutSnapshot | null;
  save(snapshot: ShellLayoutSnapshot): void;
  hydrate?(): Promise<ShellLayoutSnapshot | null>;
};

export function defaultShellLayoutSnapshot(): ShellLayoutSnapshot {
  return {
    sidebarCollapsed: false,
    expandedAppPaneWidth: null,
    collapsedAppPaneWidth: null
  };
}

export function normalizeShellLayoutSnapshot(value: unknown): ShellLayoutSnapshot | null {
  if (typeof value !== "object" || value === null) return null;

  const record = value as Partial<ShellLayoutSnapshot>;

  return {
    sidebarCollapsed: record.sidebarCollapsed === true,
    expandedAppPaneWidth: normalizePaneWidth(record.expandedAppPaneWidth),
    collapsedAppPaneWidth: normalizePaneWidth(record.collapsedAppPaneWidth)
  };
}

export function createDesktopShellLayoutStorage(): ShellLayoutStorage {
  return {
    load: () => null,
    async hydrate() {
      const snapshot = await requestDesktopShellLayout("read", {});
      return normalizeShellLayoutSnapshot(snapshot);
    },
    save(snapshot) {
      void requestDesktopShellLayout("write", snapshot).catch(() => undefined);
    }
  };
}

function normalizePaneWidth(value: unknown): number | null {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) return null;

  return value;
}

type DesktopShellLayoutRequest =
  | { source: "hpdos.shell.layout"; type: "request"; id: number; method: "read"; params: {} }
  | {
      source: "hpdos.shell.layout";
      type: "request";
      id: number;
      method: "write";
      params: ShellLayoutSnapshot;
    };

type DesktopShellLayoutResponse =
  | { source: "hpdos.shell.layout"; type: "response"; id: number; success: true; payload: unknown }
  | { source: "hpdos.shell.layout"; type: "response"; id: number; success: false; error?: string };

type ElectrobunGlobals = {
  __electrobunSendToHost?: (message: unknown) => void;
};

const pendingDesktopRequests = new Map<number, {
  resolve(value: unknown): void;
  reject(reason?: unknown): void;
}>();
let desktopRequestId = 0;
let desktopResponseHandlerInstalled = false;

function requestDesktopShellLayout(method: "read", params: {}): Promise<unknown>;
function requestDesktopShellLayout(method: "write", params: ShellLayoutSnapshot): Promise<unknown>;
function requestDesktopShellLayout(
  method: DesktopShellLayoutRequest["method"],
  params: DesktopShellLayoutRequest["params"]
): Promise<unknown> {
  const sendToHost = electrobunGlobals().__electrobunSendToHost;
  if (typeof sendToHost !== "function") {
    return Promise.reject(new Error("Electrobun host event bridge is unavailable."));
  }

  installDesktopResponseHandler();

  return new Promise((resolve, reject) => {
    const id = ++desktopRequestId;
    pendingDesktopRequests.set(id, { resolve, reject });

    const packet: DesktopShellLayoutRequest = {
      source: "hpdos.shell.layout",
      type: "request",
      id,
      method,
      params: params as never
    } as DesktopShellLayoutRequest;

    try {
      sendToHost(packet);
    } catch (error) {
      pendingDesktopRequests.delete(id);
      reject(error);
      return;
    }

    setTimeout(() => {
      const pending = pendingDesktopRequests.get(id);
      if (!pending) return;

      pendingDesktopRequests.delete(id);
      pending.reject(new Error(`Electrobun desktop request timed out: ${method}`));
    }, 5000);
  });
}

function installDesktopResponseHandler(): void {
  if (desktopResponseHandlerInstalled) return;

  window.addEventListener("hpdos-shell-layout-response", (event) => {
    if (!(event instanceof CustomEvent)) return;
    handleDesktopResponse(event.detail);
  });
  desktopResponseHandlerInstalled = true;
}

function handleDesktopResponse(message: unknown): boolean {
  if (typeof message !== "object" || message === null) return false;

  const packet = message as Partial<DesktopShellLayoutResponse>;
  if (
    packet.source !== "hpdos.shell.layout"
    || packet.type !== "response"
    || typeof packet.id !== "number"
  ) {
    return false;
  }

  const pending = pendingDesktopRequests.get(packet.id);
  if (!pending) return false;

  pendingDesktopRequests.delete(packet.id);
  if (packet.success === true) {
    pending.resolve(packet.payload);
  } else {
    pending.reject("error" in packet ? packet.error : undefined);
  }

  return true;
}

function electrobunGlobals(): ElectrobunGlobals {
  return globalThis as typeof globalThis & ElectrobunGlobals;
}
