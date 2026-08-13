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
        const codec = await page.evaluate(async () => { const base = await import("/dist/index.js"); const graph = { f32: { kind: "floating", precision: "binary32", finiteOnly: true }, array: { kind: "array", elementTypeId: "f32", minItems: 0, maxItems: 2 }, subject: { kind: "subjectReference", contractId: "hpd.auth.user-subject", contractVersion: 1, subjectIdKind: "guid", maximumSubjectIdUtf8Bytes: 36, authorityEpochBytes: 16, incarnationBytes: 16 }, record: { kind: "object", additionalProperties: false, properties: [{ name: "score", wireName: "score", typeId: "f32", required: true, nullable: false, disclosureShape: "none" }] }, create: { kind: "object", additionalProperties: false, properties: [] }, replace: { kind: "object", additionalProperties: false, properties: [] }, patch: { kind: "object", additionalProperties: false, properties: [] } }; const schema = { protocolMajor: 2, schemaGeneration: "1", digest: `sha256:${"0".repeat(64)}`, audience: "application", features: { files: false, realtime: false, batch: false, controlOperations: [] }, typeGraph: graph, reads: {}, collections: { documents: base.collection({ id: "documents", recordTypeId: "record", createTypeId: "create", replaceTypeId: "replace", patchTypeId: "patch", fields: { score: base.field("score", "score", ["equal"], "f32") }, operations: ["get"], pagination: "seek", maxPageSize: 10, vectorIndexes: {} }) } }; const client = base.createBaseClient({ schema, url: "http://base.invalid/base/", fetch: async () => new Response('{"collectionId":"documents","id":"d1","payload":{"kind":"json","json":{"score":-0}},"metadata":{}}', { headers: { "X-Correlation-ID": "browser" } }) }); const response = await client.documents.get("d1"); const subject = { subjectId: "0194f778-5cd1-7d17-ae1f-8f95b3114a20", authorityEpoch: "AAAAAAAAAAAAAAAAAAAAAA", incarnation: "AQEBAQEBAQEBAQEBAQEBAQ" }; return { encoded: base.encodeBaseJson([Math.fround(0.1), -0], "array", graph), decoded: base.decodeBaseJson("[0.1,-0]", "array", graph), encodedSubject: base.encodeBaseJson(subject, "subject", graph), decodedSubject: base.decodeBaseJson(base.encodeBaseJson(subject, "subject", graph), "subject", graph), transportPositiveZero: response.ok && response.value.payload.json.score === 0 && !Object.is(response.value.payload.json.score, -0) }; });
        assert.deepEqual(codec, { encoded: "[0.1,0]", decoded: [Math.fround(0.1), 0], encodedSubject: '{"subjectId":"0194f778-5cd1-7d17-ae1f-8f95b3114a20","authorityEpoch":"AAAAAAAAAAAAAAAAAAAAAA","incarnation":"AQEBAQEBAQEBAQEBAQEBAQ"}', decodedSubject: { subjectId: "0194f778-5cd1-7d17-ae1f-8f95b3114a20", authorityEpoch: "AAAAAAAAAAAAAAAAAAAAAA", incarnation: "AQEBAQEBAQEBAQEBAQEBAQ" }, transportPositiveZero: true });
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
