import Electrobun, { BrowserWindow, PATHS } from "electrobun/bun";
import { existsSync } from "node:fs";
import { delimiter, dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { readShellLayout, writeShellLayout, type ShellLayoutSnapshot } from "./settingsStore";

const backendUrl = process.env.HPDOS_BACKEND_URL ?? "http://127.0.0.1:4317";
const appDir = dirname(fileURLToPath(import.meta.url));
const projectDirectory = process.env.HPDOS_PROJECT_DIRECTORY ?? join(process.cwd(), "../..");
const backendDirectory = process.env.HPDOS_BACKEND_DIRECTORY ?? join(process.cwd(), "../backend");
const backendMode = process.env.HPDOS_BACKEND_MODE ?? "published";
const dotnetExecutable = process.env.HPDOS_DOTNET ?? findDotnetExecutable();
const executableName = process.platform === "win32" ? "backend.exe" : "backend";

let backendProcess: Bun.Subprocess | null = null;
let backendProcessMode: "run" | "published" | null = null;
let mainWindow: BrowserWindow | null = null;
let chromeModeInterval: Timer | null = null;
const pendingShellLayoutResponses: ShellLayoutHostResponse[] = [];

type ShellLayoutHostRequest =
  | { source: "hpdos.shell.layout"; type: "request"; id: number; method: "read"; params: {} }
  | {
      source: "hpdos.shell.layout";
      type: "request";
      id: number;
      method: "write";
      params: ShellLayoutSnapshot;
    };

type ShellLayoutHostResponse =
  | { source: "hpdos.shell.layout"; type: "response"; id: number; success: true; payload: unknown }
  | { source: "hpdos.shell.layout"; type: "response"; id: number; success: false; error?: string };

function syncWindowChromeMode(): void {
  if (!mainWindow) return;
  const isFullScreen = mainWindow.isFullScreen();
  mainWindow.webview.executeJavascript(
    `document.body.dataset.hpdWindowFullscreen = ${JSON.stringify(String(isFullScreen))};`
  );
}

function handleShellLayoutHostMessage(message: unknown): void {
  if (!isShellLayoutRequest(message)) return;

  try {
    if (message.method === "read") {
      sendShellLayoutResponse({
        source: "hpdos.shell.layout",
        type: "response",
        id: message.id,
        success: true,
        payload: readShellLayout()
      });
      return;
    }

    writeShellLayout(message.params);
    sendShellLayoutResponse({
      source: "hpdos.shell.layout",
      type: "response",
      id: message.id,
      success: true,
      payload: { success: true }
    });
  } catch (error) {
    sendShellLayoutResponse({
      source: "hpdos.shell.layout",
      type: "response",
      id: message.id,
      success: false,
      error: error instanceof Error ? error.message : String(error)
    });
  }
}

function isShellLayoutRequest(message: unknown): message is ShellLayoutHostRequest {
  if (typeof message !== "object" || message === null) return false;

  const request = message as Partial<ShellLayoutHostRequest>;
  if (
    request.source !== "hpdos.shell.layout"
    || request.type !== "request"
    || typeof request.id !== "number"
  ) {
    return false;
  }

  if (request.method === "read") return true;

  return request.method === "write" && normalizeShellLayoutSnapshot(request.params) !== null;
}

function sendShellLayoutResponse(response: ShellLayoutHostResponse): void {
  if (!mainWindow) {
    pendingShellLayoutResponses.push(response);
    return;
  }

  mainWindow.webview.executeJavascript(
    `window.dispatchEvent(new CustomEvent("hpdos-shell-layout-response", { detail: ${JSON.stringify(response)} }));`
  );
}

function flushShellLayoutResponses(): void {
  while (pendingShellLayoutResponses.length > 0) {
    const response = pendingShellLayoutResponses.shift();
    if (response) sendShellLayoutResponse(response);
  }
}

function normalizeShellLayoutSnapshot(value: unknown): ShellLayoutSnapshot | null {
  if (typeof value !== "object" || value === null) return null;

  const record = value as Partial<ShellLayoutSnapshot>;

  return {
    sidebarCollapsed: record.sidebarCollapsed === true,
    expandedAppPaneWidth: normalizePaneWidth(record.expandedAppPaneWidth),
    collapsedAppPaneWidth: normalizePaneWidth(record.collapsedAppPaneWidth)
  };
}

function normalizePaneWidth(value: unknown): number | null {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) return null;

  return value;
}

async function isBackendReady(): Promise<boolean> {
  try {
    const response = await fetch(`${backendUrl}/api/hpdos/runtime`, { method: "GET" });
    return response.ok;
  } catch {
    return false;
  }
}

async function waitForBackend(timeoutMs = 30_000): Promise<boolean> {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    if (await isBackendReady()) return true;
    await Bun.sleep(250);
  }
  return false;
}

async function startBackendIfNeeded(): Promise<void> {
  if (await isBackendReady()) return;

  if (backendMode === "run") {
    if (!dotnetExecutable) {
      console.error("dotnet was not found. Set HPDOS_DOTNET to the absolute dotnet executable path.");
      return;
    }
    backendProcess = Bun.spawn([
      dotnetExecutable,
      "run",
      "--no-launch-profile"
    ], {
      cwd: backendDirectory,
      stdout: "inherit",
      stderr: "inherit",
      env: backendEnvironment()
    });
    backendProcessMode = "run";
    return;
  }

  const executable = findBackendExecutable();
  if (!executable) {
    console.error("Published backend executable was not found. Run `bun run publish:backend`.");
    return;
  }

  backendProcess = Bun.spawn([executable.path, "--urls", backendUrl], {
    cwd: executable.cwd,
    stdout: "inherit",
    stderr: "inherit",
    env: backendEnvironment()
  });
  backendProcessMode = "published";
}

function stopBackend(): void {
  if (!backendProcess) return;
  const pid = backendProcess.pid;
  if (backendProcessMode === "run" && process.platform !== "win32") {
    try {
      Bun.spawnSync(["pkill", "-P", String(pid)]);
    } catch {
      // Best-effort cleanup for child processes created by dotnet run.
    }
  }
  try {
    backendProcess.kill();
  } catch {
    // The backend may already be gone if dotnet failed or the user stopped it.
  }
  backendProcess = null;
  backendProcessMode = null;
}

function backendEnvironment(): Record<string, string | undefined> {
  return {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: process.env.ASPNETCORE_ENVIRONMENT ?? "Development",
    DOTNET_ENVIRONMENT: process.env.DOTNET_ENVIRONMENT ?? "Development",
    Kestrel__Endpoints__Http__Url: backendUrl,
    HPDOS__ProjectDirectory: projectDirectory
  };
}

function findBackendExecutable(): { path: string; cwd: string } | null {
  if (process.env.HPDOS_BACKEND_EXECUTABLE) {
    return {
      path: process.env.HPDOS_BACKEND_EXECUTABLE,
      cwd: dirname(process.env.HPDOS_BACKEND_EXECUTABLE)
    };
  }

  const directories = [
    join(process.cwd(), "resources", "backend"),
    join(appDir, "../../resources/backend"),
    join(PATHS.RESOURCES_FOLDER, "app", "backend"),
    join(PATHS.RESOURCES_FOLDER, "backend")
  ];

  for (const directory of directories) {
    const path = join(directory, executableName);
    if (existsSync(path)) return { path, cwd: directory };
  }

  return null;
}

function findDotnetExecutable(): string | null {
  const names = process.platform === "win32" ? ["dotnet.exe", "dotnet"] : ["dotnet"];
  const pathEntries = (process.env.PATH ?? "").split(delimiter).filter(Boolean);
  const directories = [
    ...pathEntries,
    "/usr/local/share/dotnet",
    "/usr/local/bin",
    "/opt/homebrew/bin"
  ];

  for (const directory of directories) {
    for (const name of names) {
      const path = join(directory, name);
      if (existsSync(path)) return path;
    }
  }

  return null;
}

Electrobun.events.on("host-message", (event) => {
  handleShellLayoutHostMessage((event as { data?: { detail?: unknown } }).data?.detail);
});

await startBackendIfNeeded();

const ready = await waitForBackend();
const hpdosWindow = new BrowserWindow({
  title: "",
  url: ready ? backendUrl : "views://mainview/loading.html",
  frame: {
    width: 1440,
    height: 940,
    x: 120,
    y: 80
  },
  titleBarStyle: "hiddenInset",
  trafficLightOffset: {
    x: 14,
    y: 17
  }
});
mainWindow = hpdosWindow;
flushShellLayoutResponses();

hpdosWindow.on("resize", () => {
  syncWindowChromeMode();
});

setTimeout(syncWindowChromeMode, 250);
setTimeout(syncWindowChromeMode, 750);
chromeModeInterval = setInterval(syncWindowChromeMode, 500);

hpdosWindow.on("close", () => {
  if (chromeModeInterval) {
    clearInterval(chromeModeInterval);
    chromeModeInterval = null;
  }
  stopBackend();
});
Electrobun.events.on("before-quit", () => {
  stopBackend();
});

console.log(ready ? `HPD-OS desktop loaded ${backendUrl}` : "HPD-OS backend did not become ready.");
