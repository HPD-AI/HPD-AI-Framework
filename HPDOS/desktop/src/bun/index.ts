import Electrobun, { BrowserWindow, PATHS } from "electrobun/bun";
import { existsSync } from "node:fs";
import { delimiter, dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const backendUrl = process.env.HPDOS_BACKEND_URL ?? "http://127.0.0.1:4317";
const appDir = dirname(fileURLToPath(import.meta.url));
const repoRoot = process.env.HPDOS_WORKSPACE_ROOT ?? join(process.cwd(), "../..");
const backendDirectory = process.env.HPDOS_BACKEND_DIRECTORY ?? join(process.cwd(), "../backend");
const backendMode = process.env.HPDOS_BACKEND_MODE ?? "published";
const dotnetExecutable = process.env.HPDOS_DOTNET ?? findDotnetExecutable();
const executableName = process.platform === "win32" ? "backend.exe" : "backend";

let backendProcess: Bun.Subprocess | null = null;
let backendProcessMode: "run" | "published" | null = null;

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
      // Best-effort cleanup for child apphost processes created by dotnet run.
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
    HPDOS__WorkspaceRoot: repoRoot
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

await startBackendIfNeeded();

const ready = await waitForBackend();
const mainWindow = new BrowserWindow({
  title: "HPD-OS",
  url: ready ? backendUrl : "views://mainview/loading.html",
  frame: {
    width: 1440,
    height: 940,
    x: 120,
    y: 80
  },
  titleBarStyle: "default"
});

mainWindow.on("close", stopBackend);
Electrobun.events.on("before-quit", stopBackend);

console.log(ready ? `HPD-OS desktop loaded ${backendUrl}` : "HPD-OS backend did not become ready.");
