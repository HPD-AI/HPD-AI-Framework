# HPDOS App Runtime with Lima

## Goal

HPDOS should be able to launch and supervise open-source applications as first-class workspace apps. The immediate target is web-facing apps that can be opened inside an HPDOS webview:

- multi-service apps such as Penpot
- single-process server apps such as code-server
- static/WASM apps such as Godot web exports

This proposal intentionally sets aside the HPD Agent execution framework for this feature. The execution framework is still useful long-term, but Lima is a better near-term substrate for running real apps because it already provides VM lifecycle, file sharing, port forwarding, and Docker/container runtime support.

AppFlowy is explicitly out of scope for this first version. The inspected AppFlowy path is a Linux desktop GUI app using X11, DBus, GPU devices, and host networking. That is a different runtime category and should not shape the initial web app runtime.

## Runtime Categories

### `compose-web`

Multi-service web applications launched through Docker Compose inside a Lima Docker VM.

Primary example: Penpot.

Observed shape:

- compose file: `/Users/ewoof/Desktop/HPD-Agent-InternalDocs/HPDOS/Apps/Reference/penpot/docker/images/docker-compose.yaml`
- main service: `penpot-frontend`
- entrypoint: host port `9001`, mapped to container port `8080`
- dependencies: backend, exporter, MCP, Postgres, Valkey, mailcatch
- persistence: Docker volumes for Postgres and Penpot assets

This is the hardest and most valuable first target because it validates service graphs, volumes, health checks, logs, and webview launch.

### `process-web`

Single long-running server process launched inside Lima.

Primary example: code-server.

Observed shape:

- package exposes a `code-server` binary
- runtime wants Node 22
- process should bind to `0.0.0.0:<port>` inside the VM
- HPDOS opens the forwarded `127.0.0.1:<port>` URL
- auth must be configured deliberately, either disabled for trusted local-only mode or generated and stored per app install

### `static-wasm`

Static files served directly by HPDOS or by a tiny local static server.

Primary example: Godot web exports.

Observed shape:

- the reference Godot folder is engine source, not a ready app
- HPDOS should expect a completed web export folder, usually containing HTML, JS, WASM, PCK/assets, and optional service worker files
- this category does not require Lima unless isolation is explicitly requested

## Non-Goals

- Do not build our own VM/container platform in HPDOS.
- Do not force the app runtime through `ExecutionContracts.cs` yet.
- Do not support Linux desktop GUI apps in the first version.
- Do not design around Kubernetes.
- Do not make HPDOS UI know Docker, Lima, or Compose details directly.

## Architecture

```text
HPDOS UI
  -> HPDOS backend app APIs
    -> HpdosAppRuntime
      -> LocalStaticRuntime
      -> LimaProcessRuntime
      -> LimaComposeRuntime
        -> limactl
        -> Lima Docker VM
        -> Docker Compose
```

HPDOS should own a small app runtime abstraction. Lima should be treated as the first backend implementation, not leaked throughout the codebase.

The compose runner should not require Docker Desktop or a host Docker CLI. Once the HPDOS Lima Docker instance exists, HPDOS should execute Docker and Docker Compose inside that instance through `limactl shell`. This keeps Lima as the single runtime dependency for compose-backed apps.

## App Manifest

HPDOS should introduce a small manifest for launchable apps. The manifest should be concrete enough to run real apps, but not so broad that it becomes a second orchestration platform.

Example for Penpot:

```json
{
  "id": "penpot",
  "name": "Penpot",
  "kind": "compose-web",
  "runtime": "lima-docker",
  "source": "/Users/ewoof/Desktop/HPD-Agent-InternalDocs/HPDOS/Apps/Reference/penpot",
  "compose": {
    "file": "docker/images/docker-compose.yaml",
    "project": "hpdos-penpot"
  },
  "entrypoint": {
    "service": "penpot-frontend",
    "host": "127.0.0.1",
    "hostPort": 9001,
    "path": "/"
  },
  "health": {
    "type": "http",
    "url": "http://127.0.0.1:9001/"
  }
}
```

Example for code-server:

```json
{
  "id": "code-server",
  "name": "Code Server",
  "kind": "process-web",
  "runtime": "lima",
  "command": {
    "fileName": "code-server",
    "args": [
      "--bind-addr",
      "0.0.0.0:8080",
      "--auth",
      "none",
      "/workspace"
    ]
  },
  "entrypoint": {
    "host": "127.0.0.1",
    "hostPort": 8080,
    "path": "/"
  },
  "health": {
    "type": "http",
    "url": "http://127.0.0.1:8080/"
  }
}
```

Example for a Godot web export:

```json
{
  "id": "godot-export",
  "name": "Godot Export",
  "kind": "static-wasm",
  "runtime": "local",
  "static": {
    "root": "/path/to/godot-web-export",
    "index": "index.html"
  },
  "entrypoint": {
    "path": "/index.html"
  }
}
```

## Backend API Shape

The backend should expose app-oriented APIs, not raw Lima or Docker commands.

Suggested operations:

- `ListApps`
- `GetApp`
- `InstallApp`
- `StartApp`
- `StopApp`
- `RestartApp`
- `GetAppStatus`
- `GetAppLogs`
- `OpenAppEntrypoint`

Status should be simple and UI-friendly:

```text
MissingRuntime
InstallingRuntime
Stopped
Starting
Running
Degraded
Stopping
Failed
```

## Lima Runtime Responsibilities

### Runtime Detection

The backend should detect:

- whether `limactl` exists
- Lima version
- whether the HPDOS Lima instance exists
- whether it is running
- Docker socket path for the Lima Docker instance

The Docker socket can be discovered with Lima's list formatting if HPDOS later wants to support a host-side Docker CLI:

```bash
limactl list hpdos --format '{{.Dir}}/sock/docker.sock'
```

The current preferred path is to query Docker from inside Lima:

```bash
limactl shell hpdos -- docker info
```

### Runtime Creation

Create one shared HPDOS Lima Docker instance for v0:

```bash
limactl start --name hpdos template:docker
```

Later, HPDOS can support per-project or per-app instances if isolation or resource control requires it.

### Compose Launch

For `compose-web`, run Docker Compose inside the Lima Docker instance:

```bash
limactl shell hpdos -- bash -lc \
  'cd /path/to/penpot && docker compose -p hpdos-penpot -f docker/images/docker-compose.yaml up -d'
```

HPDOS should use a stable Compose project name per app to make status, logs, and cleanup deterministic.

### Process Launch

For `process-web`, run a long-lived process inside Lima with a controlled command and working directory.

The runtime should capture:

- process id or supervisor id
- stdout/stderr logs
- selected port
- start time
- exit reason

For v0, this can be implemented with a simple backend-managed process wrapper around `limactl shell`. A more durable supervisor can follow once the UX is proven.

### Static Launch

For `static-wasm`, serve files directly from HPDOS backend. This keeps static and WASM apps fast and avoids unnecessary VM startup.

Godot web exports may require correct headers for isolation features. The static server should be able to set headers such as:

- `Cross-Origin-Opener-Policy`
- `Cross-Origin-Embedder-Policy`
- `Cross-Origin-Resource-Policy`

## UI Surface

Add an Apps surface to HPDOS with:

- app list
- runtime health indicator
- start/stop/restart controls
- primary open button
- logs panel
- service list for compose apps
- entrypoint URL
- clear error messages when Lima, Docker, Compose, ports, or health checks fail

The UI should treat apps as user-facing workspace tools, not as raw container infrastructure.

## Frontend Contract

The HPDOS frontend should consume app snapshots from the backend. The backend is the lifecycle source of truth. The UI should not infer Docker, Compose, Lima, process, or port-forwarding state from raw command details.

The current frontend already has a clean pattern for this:

- core state lives in `wwwroot/src/core/hpdosState.ts`
- the app coordinator lives in `wwwroot/src/core/hpdosApp.ts`
- fetch wrappers live beside `wwwroot/src/core/hpdosWorkspaceApi.ts`
- sidebar routes are declared in `wwwroot/src/view/svelte/types.ts`
- route views live under `wwwroot/src/view/svelte`

The app runtime should follow that shape:

- add `hpdosAppsApi.ts` for `/api/hpdos/apps`
- extend `HpdosState` with app state
- extend `SidebarView` with `apps`
- add `WorkspaceAppsView.svelte`
- keep app actions on `ViewActions`, but delegate behavior to `HpdosApp`

Suggested frontend-facing types:

```ts
export type HpdosAppKind = "compose-web" | "process-web" | "static-wasm";

export type HpdosAppLifecycle =
  | "missing-runtime"
  | "not-installed"
  | "installing"
  | "stopped"
  | "starting"
  | "running"
  | "degraded"
  | "stopping"
  | "failed";

export interface HpdosLaunchableApp {
  id: string;
  name: string;
  kind: HpdosAppKind;
  lifecycle: HpdosAppLifecycle;
  entrypoint?: HpdosAppEntrypoint;
  services: HpdosAppService[];
  health?: HpdosAppHealth;
  lastError?: string;
  diagnostics?: HpdosAppDiagnostics;
}

export interface HpdosAppEntrypoint {
  url: string;
  label?: string;
}

export interface HpdosAppHealth {
  ok: boolean;
  message?: string;
  checkedAt?: string;
}

export interface HpdosAppService {
  id: string;
  name: string;
  role?: "entrypoint" | "database" | "cache" | "worker" | "mail" | "support";
  status: "unknown" | "created" | "running" | "healthy" | "unhealthy" | "exited";
  image?: string;
  ports?: HpdosAppPort[];
  lastError?: string;
}

export interface HpdosAppPort {
  host?: number;
  container: number;
  protocol?: "tcp" | "udp";
}

export interface HpdosAppDiagnostics {
  runtime?: string;
  runtimeInstance?: string;
  composeProject?: string;
  workingDirectory?: string;
}
```

`diagnostics` is intentionally optional and secondary. It can power a details drawer, logs view, or troubleshooting panel, but it should not drive the primary app list.

Suggested state addition:

```ts
export interface HpdosState {
  apps: HpdosLaunchableApp[];
  activeAppId: string;
  appsLoading: boolean;
  appError: string | null;
}
```

Suggested actions:

```ts
export type HpdosAppAction =
  | "install"
  | "start"
  | "stop"
  | "restart"
  | "open"
  | "view-logs";
```

The primary action should be derived from lifecycle:

```text
missing-runtime -> Set Up Runtime
not-installed -> Install
installing -> Installing...
stopped -> Start
starting -> Starting...
running -> Open
degraded -> Open / Restart
stopping -> Stopping...
failed -> Retry
```

## Frontend Lifecycle

The frontend should render lifecycle as a state machine reported by the backend:

```text
missing-runtime -> not-installed
not-installed -> installing -> stopped
stopped -> starting -> running
running -> degraded -> running
running -> stopping -> stopped
starting -> failed
installing -> failed
failed -> starting
failed -> installing
failed -> stopped
```

The frontend should never transition an app to `running` just because a start request returned successfully. A start request means the backend accepted the command. The backend should mark the app `running` only after the runtime and health checks confirm it.

Polling rules:

- poll every second while any app is `installing`, `starting`, `stopping`, or `degraded`
- poll every 10 to 15 seconds while visible apps are `running`
- refresh on window focus
- refresh after app actions complete
- do not stream logs unless the app detail view or logs panel is open

The UI should show three levels of information:

- app-level lifecycle: install/start/open/error
- service-level status for compose apps
- diagnostics/logs only when the user drills in

This keeps Penpot from looking like six raw containers in the main UI. The main row should be "Penpot is running" with an Open button. The detail view can explain frontend/backend/exporter/Postgres/Valkey/mailcatch.

## Frontend Placement

The Apps surface should be a peer of Sessions and Files in the existing sidebar route model:

```ts
export type SidebarView = "workspaceSessions" | "conversation" | "files" | "apps";
```

Recommended views:

- `WorkspaceAppsView.svelte`: app list, primary actions, lifecycle chips, runtime health
- `AppDetailView.svelte`: services, logs, health, diagnostics, restart/stop controls
- optional app webview surface when HPDOS is ready to host app entrypoints inside the shell

The first implementation can open the app entrypoint in the existing desktop webview/browser surface if that is easier. The contract should still call it `entrypoint.url`, not `containerUrl`, `composeUrl`, or `limaUrl`.

## Backend Placement

The backend should add app services beside the existing workspace services:

```csharp
builder.Services.AddSingleton<HpdosAppCatalogService>();
builder.Services.AddSingleton<HpdosAppRuntimeService>();
```

Suggested endpoints:

```text
GET    /api/hpdos/apps
GET    /api/hpdos/apps/{id}
POST   /api/hpdos/apps/{id}/install
POST   /api/hpdos/apps/{id}/start
POST   /api/hpdos/apps/{id}/stop
POST   /api/hpdos/apps/{id}/restart
GET    /api/hpdos/apps/{id}/logs
GET    /api/hpdos/apps/{id}/status
```

The backend response should be the same `HpdosLaunchableApp` shape used by the frontend. Logs can be separate because they are large, incremental, and only needed on demand.

## First Milestone

Build Penpot end to end:

1. Detect `limactl`.
2. Create/start the `hpdos` Lima Docker instance.
3. Discover the Lima Docker socket.
4. Start Penpot using its production compose file.
5. Wait for `http://127.0.0.1:9001/`.
6. Open Penpot in an HPDOS webview.
7. Show status, logs, services, and stop/restart controls.

Success means HPDOS can run a real multi-service open-source app without the user manually operating Docker Desktop, Compose, or terminal commands.

## Second Milestone

Add code-server as `process-web`:

1. Install or locate code-server inside Lima.
2. Launch it on a stable port.
3. Bind to `0.0.0.0` inside Lima.
4. Open the forwarded localhost URL in HPDOS.
5. Stream logs and support stop/restart.

This validates single-process server apps and workspace mounting.

## Third Milestone

Add `static-wasm`:

1. Let the user pick a static app/export folder.
2. Serve it from the HPDOS backend.
3. Open it in an HPDOS webview.
4. Add correct WASM headers.

This validates Godot-style web exports without dragging them through the VM/container path.

## Later Work

- per-app Lima instances
- app install catalog
- automatic port allocation and conflict resolution
- compose file patching for stable HPDOS ports
- persistent app data management
- backup/export app data
- update/migration flows
- desktop GUI apps through VNC/noVNC or another display bridge
- optional migration from Lima runtime to HPD execution contracts if that substrate becomes ready

## Recommendation

Use Lima directly behind an HPDOS-owned app runtime interface. Start with Penpot because it validates the hardest app class first. Keep the abstraction small, product-shaped, and based on actual observed apps rather than a generic execution model.
