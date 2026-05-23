# HPD-OS Desktop

This is a small Electrobun shell around the HPD-OS ASP.NET backend.

## Development

```bash
cd HPDOS/desktop
bun install
bun run dev
```

Development runs the backend from source with:

```bash
cd ../backend
dotnet run --no-launch-profile
```

The desktop app checks `http://127.0.0.1:4317/api/hpdos/runtime`. If the backend is already running, it reuses it. Otherwise it starts the backend and cleans it up when the desktop window closes.

## Build

```bash
bun run build
```

Build/export publishes the backend first, then bundles Electrobun. The published backend executable lives at:

```bash
resources/backend/backend
```

The backend project publishes framework-dependent output by default. That still gives the desktop shell a real executable, but avoids the slower and less predictable single-file extraction path.

Set `HPDOS_BACKEND_URL` to point the desktop shell at a different backend.
Set `HPDOS_BACKEND_EXECUTABLE` to use a specific backend binary.
Set `HPDOS_PROJECT_DIRECTORY` to choose the default project/workspace directory passed to the backend.
