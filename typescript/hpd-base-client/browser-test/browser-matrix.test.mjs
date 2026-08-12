import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { extname, join } from "node:path";
import { chromium, firefox, webkit } from "@playwright/test";
import test from "node:test";

const packageRoot = new URL("../", import.meta.url).pathname;
const frameworkRoot = new URL("../../../dotnet/HPD-Base.Framework/", import.meta.url).pathname;

test("Chromium, Firefox, and WebKit execute codecs and a real ASP.NET realtime-v2 WebSocket", { timeout: 180_000 }, async () => {
  const staticServer = createServer(async (request, response) => { try { const path = request.url === "/" ? "/index.html" : request.url; if (path === "/index.html") { response.setHeader("Content-Type", "text/html"); response.end("<!doctype html><script type=module>globalThis.ready=true</script>"); return; } const file = join(packageRoot, path); response.setHeader("Content-Type", extname(file) === ".js" ? "text/javascript" : "application/octet-stream"); response.end(await readFile(file)); } catch { response.statusCode = 404; response.end(); } });
  const staticPort = await listen(staticServer); const hostPort = await freePort();
  const host = spawn("dotnet", ["run", "-c", "Release", "--project", `${frameworkRoot}test/HPD.Base.AspNetCore.AotSmoke/HPD.Base.AspNetCore.AotSmoke.csproj`, "--", "--urls", `http://127.0.0.1:${hostPort}`], { cwd: frameworkRoot, stdio: ["ignore", "pipe", "pipe"] });
  try {
    await waitForHost(host, `http://127.0.0.1:${hostPort}/`);
    const versions = {};
    for (const [name, engine] of Object.entries({ chromium, firefox, webkit })) {
      const browser = await engine.launch({ headless: true }); versions[name] = browser.version();
      try {
        const page = await browser.newPage(); await page.goto(`http://127.0.0.1:${staticPort}/`);
        const codec = await page.evaluate(async () => { const base = await import("/dist/index.js"); const graph = { f32: { kind: "floating", precision: "binary32", finiteOnly: true }, array: { kind: "array", elementTypeId: "f32", maxItems: 2 } }; return { encoded: base.encodeBaseJson([Math.fround(0.1), -0], "array", graph), decoded: base.decodeBaseJson("[0.1,-0]", "array", graph) }; });
        assert.deepEqual(codec, { encoded: "[0.1,0]", decoded: [Math.fround(0.1), 0] });
        const websocket = await page.evaluate(url => new Promise((resolve, reject) => { const socket = new WebSocket(url); let welcomed = false; const timer = setTimeout(() => reject(new Error("websocket timeout")), 15_000); socket.onmessage = event => { if (welcomed) return; const welcome = JSON.parse(event.data); if (welcome.protocol !== 2 || welcome.kind !== "welcome") reject(new Error("invalid welcome")); else { welcomed = true; socket.send('{"protocol":2,"kind":"heartbeat","kind":"heartbeat","connectionId":"x","connectionEpoch":"y","heartbeatId":"z"}'); } }; socket.onclose = event => { clearTimeout(timer); resolve({ code: event.code, reason: event.reason, welcomed }); }; socket.onerror = () => undefined; }), `ws://127.0.0.1:${hostPort}/base/realtime/v2/socket`);
        assert.equal(websocket.welcomed, true);
        assert.equal(websocket.code, 1008); assert.equal(websocket.reason, "BASE realtime protocol failure.");
      } finally { await browser.close(); }
    }
    assert.deepEqual(Object.keys(versions), ["chromium", "firefox", "webkit"]); process.stdout.write(`browser versions ${JSON.stringify(versions)}\n`);
  } finally { host.kill("SIGTERM"); staticServer.close(); await Promise.race([new Promise(resolve => host.once("exit", resolve)), new Promise(resolve => setTimeout(resolve, 5_000))]); }
});

async function listen(server) { return await new Promise((resolve, reject) => { server.once("error", reject); server.listen(0, "127.0.0.1", () => resolve(server.address().port)); }); }
async function freePort() { const server = createServer(); const port = await listen(server); await new Promise(resolve => server.close(resolve)); return port; }
async function waitForHost(process, url) { let stderr = ""; process.stderr.setEncoding("utf8"); process.stderr.on("data", chunk => { stderr += chunk; }); const end = Date.now() + 60_000; while (Date.now() < end) { if (process.exitCode !== null) throw new Error(`host exited ${process.exitCode}: ${stderr}`); try { const response = await fetch(url); if (response.ok) return; } catch { } await new Promise(resolve => setTimeout(resolve, 100)); } throw new Error(`host startup timed out: ${stderr}`); }
