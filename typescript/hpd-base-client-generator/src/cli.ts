#!/usr/bin/env node
import { readFile } from "node:fs/promises";
import { generate, parseSnapshot } from "./generator.js";
import type { GenerationSnapshot } from "./types.js";

const arguments_ = process.argv.slice(2);
if (arguments_[0] !== "generate") fail("Expected the generate command.");
validateArguments(arguments_);
const snapshotPath = option("--snapshot");
const url = option("--url");
const out = option("--out");
const audienceText = option("--audience");
const audience = audienceText === "control-plane" ? "controlPlane" : audienceText === "application" ? "application" : undefined;
if (audienceText !== undefined && audience === undefined) fail("Audience must be application or control-plane.");
if (out === undefined || (snapshotPath === undefined) === (url === undefined)) fail("Supply exactly one snapshot or URL input and one output.");
if (url !== undefined && audience === undefined) fail("Remote generation requires an audience assertion.");
let snapshot: GenerationSnapshot;
if (snapshotPath !== undefined) snapshot = parseSnapshot(await readFile(snapshotPath, "utf8"));
else {
  const token = process.env.HPD_BASE_ACCESS_TOKEN;
  const response = await fetch(new URL("client-generation", ensureSlash(new URL(url!))), { headers: token === undefined ? {} : { Authorization: `Bearer ${token}` }, redirect: "error" });
  if (!response.ok) fail("The generation endpoint rejected the request.");
  snapshot = parseSnapshot(await readBounded(response, 4 * 1024 * 1024));
}
await generate({ snapshot, out, ...(audience === undefined ? {} : { expectedAudience: audience }) });

function option(name: string): string | undefined { const index = arguments_.indexOf(name); return index < 0 ? undefined : arguments_[index + 1]; }
function validateArguments(values: readonly string[]): void {
  const accepted = new Set(["--snapshot", "--url", "--out", "--audience"]);
  const seen = new Set<string>();
  for (let index = 1; index < values.length; index += 2) {
    const name = values[index]; const value = values[index + 1];
    if (name === undefined || !accepted.has(name) || seen.has(name) || value === undefined || value.startsWith("--")) fail("The generator arguments are invalid.");
    seen.add(name);
  }
}
async function readBounded(response: Response, maximumBytes: number): Promise<string> {
  const declared = response.headers.get("content-length");
  if (declared !== null && (!/^\d+$/u.test(declared) || Number(declared) > maximumBytes)) fail("The generation snapshot is too large.");
  if (response.body === null) fail("The generation snapshot is missing.");
  const reader = response.body.getReader(); const decoder = new TextDecoder("utf-8", { fatal: true });
  let size = 0; let value = "";
  try {
    while (true) { const item = await reader.read(); if (item.done) break; size += item.value.byteLength; if (size > maximumBytes) { await reader.cancel(); fail("The generation snapshot is too large."); } value += decoder.decode(item.value, { stream: true }); }
    value += decoder.decode(); return value;
  } catch { fail("The generation snapshot is invalid."); }
}
function fail(message: string): never { process.stderr.write(`${message}\n`); process.exit(2); }
function ensureSlash(url: URL): URL { if (!url.pathname.endsWith("/")) url.pathname += "/"; return url; }
