export type DesktopHostRequest<Method extends string, Params> = {
  source: string;
  type: "request";
  id: number;
  method: Method;
  params: Params;
};

type DesktopHostResponse =
  | { source: string; type: "response"; id: number; success: true; payload: unknown }
  | { source: string; type: "response"; id: number; success: false; error?: string };

type ElectrobunGlobals = {
  __electrobunSendToHost?: (message: unknown) => void;
};

const pendingDesktopRequests = new Map<number, {
  source: string;
  resolve(value: unknown): void;
  reject(reason?: unknown): void;
}>();
let desktopRequestId = 0;
let desktopResponseHandlerInstalled = false;
const desktopHostRequestTimeoutMs = 120_000;

export function requestDesktopHost<Method extends string, Params>(
  source: string,
  method: Method,
  params: Params
): Promise<unknown> {
  const sendToHost = electrobunGlobals().__electrobunSendToHost;
  if (typeof sendToHost !== "function") {
    return Promise.reject(new Error("Electrobun host event bridge is unavailable."));
  }

  installDesktopResponseHandler();

  return new Promise((resolve, reject) => {
    const id = ++desktopRequestId;
    pendingDesktopRequests.set(id, { source, resolve, reject });

    const packet: DesktopHostRequest<Method, Params> = {
      source,
      type: "request",
      id,
      method,
      params
    };

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
      pending.reject(new Error(`Electrobun desktop request timed out: ${source}:${method}`));
    }, desktopHostRequestTimeoutMs);
  });
}

export const requestDesktopSettings = requestDesktopHost;

function installDesktopResponseHandler(): void {
  if (desktopResponseHandlerInstalled) return;

  window.addEventListener("hpdos-desktop-host-response", (event) => {
    if (!(event instanceof CustomEvent)) return;
    handleDesktopResponse(event.detail);
  });
  desktopResponseHandlerInstalled = true;
}

function handleDesktopResponse(message: unknown): boolean {
  if (typeof message !== "object" || message === null) return false;

  const packet = message as Partial<DesktopHostResponse>;
  if (
    typeof packet.source !== "string"
    || packet.type !== "response"
    || typeof packet.id !== "number"
  ) {
    return false;
  }

  const pending = pendingDesktopRequests.get(packet.id);
  if (!pending || pending.source !== packet.source) return false;

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
