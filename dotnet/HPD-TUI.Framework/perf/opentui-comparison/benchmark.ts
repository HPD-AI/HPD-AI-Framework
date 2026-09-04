import { pathToFileURL } from "node:url"

const reference = process.env.OPENTUI_REFERENCE
if (!reference) throw new Error("Set OPENTUI_REFERENCE to the checked-out OpenTUI repository.")
const iterations = Number.parseInt(process.env.BENCHMARK_ITERATIONS ?? "1000", 10)
const warmup = Number.parseInt(process.env.BENCHMARK_WARMUP ?? "100", 10)
const core = await import(pathToFileURL(`${reference}/packages/core/src/index.ts`).href)
const testing = await import(pathToFileURL(`${reference}/packages/core/src/testing.ts`).href)
const react = await import("react")
const reactHost = await import(pathToFileURL(`${reference}/packages/react/src/index.ts`).href)

type Sample = { durationNs: number; outputBytes: number }
type Result = {
  adapter: "opentui-core" | "opentui-react"
  scenario: string
  setupNs: number
  meanNs: number
  medianNs: number
  p95Ns: number
  outputBytes: number
  heapDeltaBytes: number
}

function summarize(adapter: Result["adapter"], scenario: string, setupNs: number, samples: Sample[], heapDeltaBytes: number): Result {
  const sorted = samples.map(x => x.durationNs).sort((a, b) => a - b)
  return {
    adapter,
    scenario,
    setupNs,
    meanNs: sorted.reduce((a, b) => a + b, 0) / sorted.length,
    medianNs: sorted[Math.floor(sorted.length * 0.5)]!,
    p95Ns: sorted[Math.min(sorted.length - 1, Math.ceil(sorted.length * 0.95) - 1)]!,
    outputBytes: samples.reduce((n, x) => n + x.outputBytes, 0),
    heapDeltaBytes,
  }
}

async function measure(adapter: Result["adapter"], scenario: string, setupNs: number, mutate: (i: number) => void, render: () => Promise<void>, capture: () => string) {
  for (let i = 0; i < warmup; i++) { mutate(i); await render() }
  Bun.gc(true)
  const before = process.memoryUsage().heapUsed
  const samples: Sample[] = []
  for (let i = 0; i < iterations; i++) {
    mutate(i)
    const start = Bun.nanoseconds()
    await render()
    samples.push({ durationNs: Bun.nanoseconds() - start, outputBytes: Buffer.byteLength(capture()) })
  }
  Bun.gc(true)
  return summarize(adapter, scenario, setupNs, samples, process.memoryUsage().heapUsed - before)
}

async function runCore() {
  const setupStart = Bun.nanoseconds()
  const setup = await testing.createTestRenderer({ width: 120, height: 40, useThread: false, maxFps: Number.POSITIVE_INFINITY })
  const row0 = new core.TextRenderable(setup.renderer, { content: "alpha", position: "absolute", top: 0, left: 0 })
  const row39 = new core.TextRenderable(setup.renderer, { content: "omega", position: "absolute", top: 39, left: 0 })
  setup.renderer.root.add(row0); setup.renderer.root.add(row39)
  const setupNs = Bun.nanoseconds() - setupStart
  const results = []
  results.push(await measure("opentui-core", "warm-no-op-120x40", setupNs, () => {}, setup.renderOnce, setup.captureCharFrame))
  results.push(await measure("opentui-core", "one-row-mutation-120x40", setupNs, i => { row0.content = i % 2 ? "alpha" : "Alpha" }, setup.renderOnce, setup.captureCharFrame))
  results.push(await measure("opentui-core", "two-disjoint-rows-120x40", setupNs, i => { row0.content = i % 2 ? "alpha" : "Alpha"; row39.content = i % 2 ? "omega" : "Omega" }, setup.renderOnce, setup.captureCharFrame))
  setup.renderer.destroy()
  return results
}

async function runReact() {
  const setupStart = Bun.nanoseconds()
  const setup = await testing.createTestRenderer({ width: 120, height: 40, useThread: false, maxFps: Number.POSITIVE_INFINITY })
  const root = reactHost.createRoot(setup.renderer)
  const renderTree = (first: string, last: string) => root.render(react.createElement(react.Fragment, null,
    react.createElement("text", { position: "absolute", top: 0, left: 0 }, first),
    react.createElement("text", { position: "absolute", top: 39, left: 0 }, last)))
  renderTree("alpha", "omega"); await setup.renderOnce()
  const setupNs = Bun.nanoseconds() - setupStart
  const results = []
  results.push(await measure("opentui-react", "warm-no-op-120x40", setupNs, () => renderTree("alpha", "omega"), setup.renderOnce, setup.captureCharFrame))
  results.push(await measure("opentui-react", "one-row-mutation-120x40", setupNs, i => renderTree(i % 2 ? "alpha" : "Alpha", "omega"), setup.renderOnce, setup.captureCharFrame))
  results.push(await measure("opentui-react", "two-disjoint-rows-120x40", setupNs, i => renderTree(i % 2 ? "alpha" : "Alpha", i % 2 ? "omega" : "Omega"), setup.renderOnce, setup.captureCharFrame))
  root.unmount(); setup.renderer.destroy()
  return results
}

const results = [...await runCore(), ...await runReact()]
console.log(JSON.stringify({
  schema: 1,
  runtime: Bun.version,
  opentuiCommit: Bun.spawnSync(["git", "-C", reference, "rev-parse", "HEAD"]).stdout.toString().trim(),
  dimensions: { width: 120, height: 40 },
  warmup,
  iterations,
  note: "outputBytes is the UTF-8 size of the memory-rendered character frame captured after each operation.",
  results,
}, null, 2))
