import Electrobun, { BrowserWindow, PATHS, Utils } from "electrobun/bun";
import { existsSync } from "node:fs";
import { delimiter, dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import {
  normalizeChatLayoutSnapshot,
  normalizeProviderModelUiState,
  normalizeShellSnapshot,
  readChatLayout,
  readChatProviderModel,
  readShell,
  writeChatLayout,
  writeChatProviderModel,
  writeShell,
  type ChatLayoutSnapshot,
  type ProviderModelUiState,
  type ShellSnapshot
} from "./settingsStore";

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
const pendingDesktopHostResponses: DesktopHostResponse[] = [];

type DesktopHostRequest =
  | { source: "hpdos.shell"; type: "request"; id: number; method: "read"; params: {} }
  | { source: "hpdos.shell"; type: "request"; id: number; method: "write"; params: ShellSnapshot }
  | { source: "hpdos.chat.layout"; type: "request"; id: number; method: "read"; params: {} }
  | {
      source: "hpdos.chat.layout";
      type: "request";
      id: number;
      method: "write";
      params: ChatLayoutSnapshot;
    }
  | { source: "hpdos.chat.providerModel.v1"; type: "request"; id: number; method: "read"; params: {} }
  | {
      source: "hpdos.chat.providerModel.v1";
      type: "request";
      id: number;
      method: "write";
      params: ProviderModelUiState;
    }
  | { source: "hpdos.workspace.dialog"; type: "request"; id: number; method: "pickDirectories"; params: {} };

type DesktopHostResponse =
  | { source: DesktopHostRequest["source"]; type: "response"; id: number; success: true; payload: unknown }
  | { source: DesktopHostRequest["source"]; type: "response"; id: number; success: false; error?: string };

function syncWindowChromeMode(): void {
  if (!mainWindow) return;
  const isFullScreen = mainWindow.isFullScreen();
  mainWindow.webview.executeJavascript(
    `document.body.dataset.hpdWindowFullscreen = ${JSON.stringify(String(isFullScreen))};`
  );
}

async function handleDesktopHostMessage(message: unknown): Promise<void> {
  if (!isDesktopHostRequest(message)) return;

  try {
    if (message.source === "hpdos.workspace.dialog") {
      sendDesktopHostResponse({
        source: message.source,
        type: "response",
        id: message.id,
        success: true,
        payload: await pickWorkspaceDirectories()
      });
      return;
    }

    if (message.method === "read") {
      sendDesktopHostResponse({
        source: message.source,
        type: "response",
        id: message.id,
        success: true,
        payload: readDesktopSettingsPayload(message.source)
      });
      return;
    }

    if (message.source === "hpdos.shell") {
      writeShell(message.params as ShellSnapshot);
    } else if (message.source === "hpdos.chat.layout") {
      writeChatLayout(message.params as ChatLayoutSnapshot);
    } else {
      writeChatProviderModel(message.params as ProviderModelUiState);
    }

    sendDesktopHostResponse({
      source: message.source,
      type: "response",
      id: message.id,
      success: true,
      payload: { success: true }
    });
  } catch (error) {
    sendDesktopHostResponse({
      source: message.source,
      type: "response",
      id: message.id,
      success: false,
      error: error instanceof Error ? error.message : String(error)
    });
  }
}

function isDesktopHostRequest(message: unknown): message is DesktopHostRequest {
  if (typeof message !== "object" || message === null) return false;

  const request = message as Partial<DesktopHostRequest>;
  if (
    request.source !== "hpdos.shell"
    && request.source !== "hpdos.chat.layout"
    && request.source !== "hpdos.chat.providerModel.v1"
    && request.source !== "hpdos.workspace.dialog"
  ) {
    return false;
  }

  if (request.type !== "request" || typeof request.id !== "number") {
    return false;
  }

  if (request.source === "hpdos.workspace.dialog") {
    return request.method === "pickDirectories";
  }

  if (request.method === "read") return true;

  if (request.method !== "write") return false;

  switch (request.source) {
    case "hpdos.shell":
      return normalizeShellSnapshot(request.params) !== null;
    case "hpdos.chat.layout":
      return normalizeChatLayoutSnapshot(request.params) !== null;
    case "hpdos.chat.providerModel.v1":
      return normalizeProviderModelUiState(request.params) !== null;
  }
}

function readDesktopSettingsPayload(source: Exclude<DesktopHostRequest["source"], "hpdos.workspace.dialog">): unknown {
  switch (source) {
    case "hpdos.shell":
      return readShell();
    case "hpdos.chat.layout":
      return readChatLayout();
    case "hpdos.chat.providerModel.v1":
      return readChatProviderModel();
  }
}

async function pickWorkspaceDirectories(): Promise<string[]> {
  const paths = await Utils.openFileDialog({
    startingFolder: process.env.HOME || projectDirectory,
    allowedFileTypes: "*",
    canChooseFiles: false,
    canChooseDirectory: true,
    allowsMultipleSelection: true
  });

  return paths.map((path) => path.trim()).filter(Boolean);
}

function sendDesktopHostResponse(response: DesktopHostResponse): void {
  if (!mainWindow) {
    pendingDesktopHostResponses.push(response);
    return;
  }

  mainWindow.webview.executeJavascript(
    `window.dispatchEvent(new CustomEvent("hpdos-desktop-host-response", { detail: ${JSON.stringify(response)} }));`
  );
}

function flushDesktopHostResponses(): void {
  while (pendingDesktopHostResponses.length > 0) {
    const response = pendingDesktopHostResponses.shift();
    if (response) sendDesktopHostResponse(response);
  }
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
  void handleDesktopHostMessage((event as { data?: { detail?: unknown } }).data?.detail);
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
flushDesktopHostResponses();

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
