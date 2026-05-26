import { afterEach, describe, expect, test } from "bun:test";
import { mkdirSync, mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

const servers = new Set();

afterEach(async () => {
  await Promise.all([...servers].map((server) => server.stop()));
  servers.clear();
});

function randomPort() {
  return 46000 + Math.floor(Math.random() * 10000);
}

function startBackend(options = {}) {
  const tempRoot = mkdtempSync(join(tmpdir(), "hpdos-terminal-api-"));
  const workspaceRoot = join(tempRoot, "workspace");
  const dataRoot = join(tempRoot, "data");
  const port = randomPort();
  const baseUrl = `http://127.0.0.1:${port}`;
  const bun = globalThis.process?.execPath ?? "bun";
  mkdirSync(workspaceRoot, { recursive: true });
  const child = Bun.spawn([
    "dotnet",
    "run",
    "--no-restore",
    "--no-launch-profile",
    "--project",
    "backend.csproj",
    "--urls",
    baseUrl
  ], {
    cwd: import.meta.dir,
    env: {
      ...Bun.env,
      HPDOS_BUN: bun,
      HPDOS__DataRoot: dataRoot,
      HPDOS__ProjectDirectory: workspaceRoot,
      HPDOS__ProjectName: "Terminal API Test",
      Kestrel__Endpoints__Http__Url: baseUrl,
      DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE: "false",
      ASPNETCORE_ENVIRONMENT: "Development",
      ...(options.env ?? {})
    },
    stdout: "pipe",
    stderr: "pipe"
  });

  const logs = [];
  readLines(child.stdout, (line) => logs.push(line));
  readLines(child.stderr, (line) => logs.push(line));

  function readLines(stream, onLine) {
    const reader = stream.getReader();
    const decoder = new TextDecoder();
    let pending = "";
    void (async () => {
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        pending += decoder.decode(value, { stream: true });
        let index = pending.indexOf("\n");
        while (index >= 0) {
          const line = pending.slice(0, index).trim();
          pending = pending.slice(index + 1);
          if (line) onLine(line);
          index = pending.indexOf("\n");
        }
      }
    })();
  }

  const server = {
    baseUrl,
    workspaceRoot,
    logs,
    async ready() {
      const deadline = Date.now() + 15000;
      let lastError;
      while (Date.now() < deadline) {
        try {
          const response = await fetch(`${baseUrl}/health`);
          if (response.ok) return;
        } catch (error) {
          lastError = error;
        }
        if (child.exitCode !== null) break;
        await Bun.sleep(100);
      }
      throw new Error(`HPDOS backend did not become ready: ${lastError?.message ?? ""}\n${logs.join("\n")}`);
    },
    async stop() {
      try {
        child.kill();
      } catch {
        // Best effort.
      }
      await child.exited.catch(() => {});
      rmSync(tempRoot, { recursive: true, force: true });
    }
  };
  servers.add(server);
  return server;
}

async function json(response) {
  const text = await response.text();
  return text ? JSON.parse(text) : undefined;
}

function connectTerminal(baseUrl, terminalId, ticket, cursor = 0) {
  const events = [];
  const waiters = [];
  const url = `${baseUrl.replace("http://", "ws://")}/api/hpdos/terminals/${terminalId}/connect?ticket=${encodeURIComponent(ticket)}&cursor=${cursor}`;
  const socket = new WebSocket(url);

  socket.addEventListener("message", (message) => {
    const event = JSON.parse(String(message.data));
    events.push(event);
    for (const waiter of [...waiters]) {
      if (!waiter.predicate(event)) continue;
      waiters.splice(waiters.indexOf(waiter), 1);
      waiter.resolve(event);
    }
  });

  return {
    events,
    socket,
    waitFor(predicate, timeoutMs = 5000) {
      const existing = events.find(predicate);
      if (existing) return Promise.resolve(existing);
      return new Promise((resolve, reject) => {
        const timeout = setTimeout(() => {
          const index = waiters.indexOf(waiter);
          if (index >= 0) waiters.splice(index, 1);
          reject(new Error(`Timed out waiting for websocket event. Seen: ${JSON.stringify(events)}`));
        }, timeoutMs);
        const waiter = {
          predicate,
          resolve: (event) => {
            clearTimeout(timeout);
            resolve(event);
          }
        };
        waiters.push(waiter);
      });
    },
    waitOpen(timeoutMs = 5000) {
      if (socket.readyState === WebSocket.OPEN) return Promise.resolve();
      return new Promise((resolve, reject) => {
        const timeout = setTimeout(() => reject(new Error("Timed out waiting for websocket open.")), timeoutMs);
        socket.addEventListener("open", () => {
          clearTimeout(timeout);
          resolve();
        }, { once: true });
        socket.addEventListener("error", () => {
          clearTimeout(timeout);
          reject(new Error("Terminal websocket failed to open."));
        }, { once: true });
      });
    },
    waitClosed(timeoutMs = 5000) {
      if (socket.readyState === WebSocket.CLOSED) return Promise.resolve({ code: 1005, reason: "" });
      return new Promise((resolve, reject) => {
        const timeout = setTimeout(() => reject(new Error("Timed out waiting for websocket close.")), timeoutMs);
        socket.addEventListener("close", (event) => {
          clearTimeout(timeout);
          resolve({ code: event.code, reason: event.reason });
        }, { once: true });
      });
    },
    close() {
      try {
        socket.close();
      } catch {
        // Best effort.
      }
    }
  };
}

describe("terminal API", () => {
  test("creates a workspace terminal and round-trips WebSocket IO with replay", async () => {
    const server = startBackend();
    await server.ready();

    const initial = await json(await fetch(`${server.baseUrl}/api/hpdos/terminals`));
    expect(initial).toEqual([]);

    const missingGet = await fetch(`${server.baseUrl}/api/hpdos/terminals/missing`);
    expect(missingGet.status).toBe(404);

    const missingDelete = await fetch(`${server.baseUrl}/api/hpdos/terminals/missing`, { method: "DELETE" });
    expect(missingDelete.status).toBe(404);

    const outside = await fetch(`${server.baseUrl}/api/hpdos/terminals`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ path: "../" })
    });
    expect(outside.status).toBe(400);

    const created = await json(await fetch(`${server.baseUrl}/api/hpdos/terminals`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ title: "api test", shell: "sh", cols: 80, rows: 24 })
    }));
    expect(created.title).toBe("api test");
    expect(created.cwd).toBe(server.workspaceRoot);
    expect(created.workspaceId).toBeTruthy();
    expect(created.rootId).toBe("default");

    const renamed = await json(await fetch(`${server.baseUrl}/api/hpdos/terminals/${created.id}`, {
      method: "PATCH",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ title: "renamed terminal" })
    }));
    expect(renamed.title).toBe("renamed terminal");

    const fetched = await json(await fetch(`${server.baseUrl}/api/hpdos/terminals/${created.id}`));
    expect(fetched.title).toBe("renamed terminal");

    const resized = await fetch(`${server.baseUrl}/api/hpdos/terminals/${created.id}/resize?cols=120&rows=32`, {
      method: "POST"
    });
    expect(resized.status).toBe(204);

    const token = await json(await fetch(`${server.baseUrl}/api/hpdos/terminals/${created.id}/connect-token`, {
      method: "POST"
    }));
    expect(token.ticket).toBeTruthy();

    const invalid = connectTerminal(server.baseUrl, created.id, "not-a-valid-ticket", 0);
    await invalid.waitClosed();

    const connection = connectTerminal(server.baseUrl, created.id, token.ticket, 0);
    await connection.waitOpen();
    const ready = await connection.waitFor((event) => event.type === "ready");
    expect(ready.terminal.id).toBe(created.id);

    const reused = connectTerminal(server.baseUrl, created.id, token.ticket, 0);
    await reused.waitClosed();

    connection.socket.send(JSON.stringify({ type: "input", data: "echo hpdos-terminal-api\nexit\n" }));
    const output = await connection.waitFor(
      (event) => event.type === "output" && String(event.data).includes("hpdos-terminal-api")
    );
    expect(output.cursor).toBeGreaterThan(0);
    await connection.waitFor((event) => event.type === "exit");
    connection.close();

    const replayToken = await json(await fetch(`${server.baseUrl}/api/hpdos/terminals/${created.id}/connect-token`, {
      method: "POST"
    }));
    const replay = connectTerminal(server.baseUrl, created.id, replayToken.ticket, 0);
    await replay.waitOpen();
    await replay.waitFor((event) => event.type === "ready");
    const replayed = await replay.waitFor(
      (event) => event.type === "output" && event.replay === true && String(event.data).includes("hpdos-terminal-api")
    );
    expect(replayed.cursor).toBeGreaterThanOrEqual(output.cursor);
    replay.close();

    const missingResize = await fetch(`${server.baseUrl}/api/hpdos/terminals/missing/resize?cols=80&rows=24`, {
      method: "POST"
    });
    expect(missingResize.status).toBe(404);

    const deleted = await fetch(`${server.baseUrl}/api/hpdos/terminals/${created.id}`, { method: "DELETE" });
    expect(deleted.status).toBe(204);
  }, 30000);

  test("streams output to multiple subscribers and closes them when deleted", async () => {
    const server = startBackend();
    await server.ready();

    const created = await json(await fetch(`${server.baseUrl}/api/hpdos/terminals`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ title: "subscribers", shell: "sh" })
    }));

    const firstToken = await json(await fetch(`${server.baseUrl}/api/hpdos/terminals/${created.id}/connect-token`, {
      method: "POST"
    }));
    const secondToken = await json(await fetch(`${server.baseUrl}/api/hpdos/terminals/${created.id}/connect-token`, {
      method: "POST"
    }));
    const first = connectTerminal(server.baseUrl, created.id, firstToken.ticket, 0);
    const second = connectTerminal(server.baseUrl, created.id, secondToken.ticket, 0);
    await Promise.all([first.waitOpen(), second.waitOpen()]);
    await Promise.all([
      first.waitFor((event) => event.type === "ready"),
      second.waitFor((event) => event.type === "ready")
    ]);

    first.socket.send(JSON.stringify({ type: "input", data: "echo hpdos-broadcast\n" }));
    await Promise.all([
      first.waitFor((event) => event.type === "output" && String(event.data).includes("hpdos-broadcast")),
      second.waitFor((event) => event.type === "output" && String(event.data).includes("hpdos-broadcast"))
    ]);

    const deleted = await fetch(`${server.baseUrl}/api/hpdos/terminals/${created.id}`, { method: "DELETE" });
    expect(deleted.status).toBe(204);
    await Promise.all([first.waitClosed(), second.waitClosed()]);
  }, 30000);

  test("reports truncation when reconnecting from an old cursor", async () => {
    const server = startBackend({ env: { HPDOS__TerminalBufferLimitBytes: "1024" } });
    await server.ready();

    const created = await json(await fetch(`${server.baseUrl}/api/hpdos/terminals`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ title: "truncation", shell: "sh" })
    }));
    const token = await json(await fetch(`${server.baseUrl}/api/hpdos/terminals/${created.id}/connect-token`, {
      method: "POST"
    }));
    const connection = connectTerminal(server.baseUrl, created.id, token.ticket, 0);
    await connection.waitOpen();
    await connection.waitFor((event) => event.type === "ready");
    connection.socket.send(JSON.stringify({
      type: "input",
      data: "i=0; while [ $i -lt 120 ]; do echo hpdos-truncate-$i-abcdefghijklmnopqrstuvwxyz; i=$((i+1)); done\n"
    }));
    await connection.waitFor((event) => event.type === "output" && event.cursor > 1600);
    connection.close();

    const replayToken = await json(await fetch(`${server.baseUrl}/api/hpdos/terminals/${created.id}/connect-token`, {
      method: "POST"
    }));
    const replay = connectTerminal(server.baseUrl, created.id, replayToken.ticket, 0);
    await replay.waitOpen();
    const ready = await replay.waitFor((event) => event.type === "ready");
    expect(ready.truncated).toBe(true);
    expect(ready.oldestCursor).toBeGreaterThan(0);
    const replayed = await replay.waitFor((event) => event.type === "output" && event.replay === true);
    expect(String(replayed.data)).not.toContain("hpdos-truncate-0-");
    replay.close();

    const deleted = await fetch(`${server.baseUrl}/api/hpdos/terminals/${created.id}`, { method: "DELETE" });
    expect(deleted.status).toBe(204);
  }, 30000);
});
