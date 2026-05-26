import { afterEach, describe, expect, test } from "bun:test";

const helpers = new Set();

afterEach(async () => {
  await Promise.all([...helpers].map((helper) => helper.stop()));
  helpers.clear();
});

function startHelper() {
  const bun = globalThis.process?.execPath ?? "bun";
  const child = Bun.spawn([bun, "terminal-helper.ts"], {
    cwd: import.meta.dir,
    stdin: "pipe",
    stdout: "pipe",
    stderr: "pipe"
  });
  const events = [];
  const waiters = [];
  let stopped = false;

  readLines(child.stdout, (line) => {
    const event = JSON.parse(line);
    events.push(event);
    for (const waiter of [...waiters]) {
      if (!waiter.predicate(event)) continue;
      waiters.splice(waiters.indexOf(waiter), 1);
      waiter.resolve(event);
    }
  });
  readLines(child.stderr, () => {
    // Helper stderr is diagnostic only; assertions should use JSON events.
  });

  function readLines(stream, onLine) {
    const reader = stream.getReader();
    const decoder = new TextDecoder();
    let pending = "";
    void (async () => {
      while (!stopped) {
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

  const helper = {
    events,
    send(command) {
      child.stdin.write(`${JSON.stringify(command)}\n`);
      child.stdin.flush();
    },
    waitFor(predicate, timeoutMs = 2000) {
      const existing = events.find(predicate);
      if (existing) return Promise.resolve(existing);
      return new Promise((resolve, reject) => {
        const timeout = setTimeout(() => {
          const index = waiters.indexOf(waiter);
          if (index >= 0) waiters.splice(index, 1);
          reject(new Error(`Timed out waiting for helper event. Seen: ${JSON.stringify(events)}`));
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
    async stop() {
      stopped = true;
      try {
        child.stdin.end();
      } catch {
        // Best effort.
      }
      try {
        child.kill();
      } catch {
        // Best effort.
      }
      await child.exited.catch(() => {});
    }
  };
  helpers.add(helper);
  return helper;
}

describe("terminal-helper", () => {
  test("creates a PTY, accepts input, resizes, and emits exit", async () => {
    const helper = startHelper();
    helper.send({
      type: "create",
      terminalId: "term_protocol",
      command: "/bin/sh",
      args: [],
      cwd: import.meta.dir,
      cols: 80,
      rows: 24
    });

    const created = await helper.waitFor((event) => event.type === "created" && event.terminalId === "term_protocol");
    expect(typeof created.pid).toBe("number");

    helper.send({ type: "resize", terminalId: "term_protocol", cols: 100, rows: 30 });
    helper.send({ type: "write", terminalId: "term_protocol", data: "echo hpdos-helper-protocol\nexit\n" });

    const output = await helper.waitFor(
      (event) => event.type === "output"
        && event.terminalId === "term_protocol"
        && String(event.data).includes("hpdos-helper-protocol")
    );
    expect(output.data).toContain("hpdos-helper-protocol");

    const exited = await helper.waitFor((event) => event.type === "exit" && event.terminalId === "term_protocol");
    expect(exited.exitCode).toBe(0);
  });

  test("reports protocol errors as JSON events", async () => {
    const helper = startHelper();
    helper.send({ type: "write", terminalId: "missing", data: "hello\n" });

    const error = await helper.waitFor((event) => event.type === "error" && event.terminalId === "missing");
    expect(error.message).toContain("terminal not found");
  });

  test("kills a running PTY and emits exit", async () => {
    const helper = startHelper();
    helper.send({
      type: "create",
      terminalId: "term_kill",
      command: "/bin/sh",
      args: ["-c", "while true; do sleep 1; done"],
      cwd: import.meta.dir,
      cols: 80,
      rows: 24
    });

    await helper.waitFor((event) => event.type === "created" && event.terminalId === "term_kill");
    helper.send({ type: "kill", terminalId: "term_kill" });

    const exited = await helper.waitFor((event) => event.type === "exit" && event.terminalId === "term_kill");
    expect(exited.terminalId).toBe("term_kill");
  });

  test("reports invalid create commands as JSON events", async () => {
    const helper = startHelper();
    helper.send({
      type: "create",
      terminalId: "term_bad_command",
      command: "/definitely/not/hpdos/missing",
      args: [],
      cwd: import.meta.dir,
      cols: 80,
      rows: 24
    });

    const error = await helper.waitFor((event) => event.type === "error" && event.terminalId === "term_bad_command");
    expect(error.message).toMatch(/enoent|not found|no such file|spawn/i);
  });
});
