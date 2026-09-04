import { pathToFileURL } from "node:url"
import { Writable } from "node:stream"

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
  schema: "hpd.tui.framework-comparison.v1"
  adapter: "opentui-core" | "opentui-react"
  scenario: string
  setupNs: number
  meanNs: number
  medianNs: number
  p95Ns: number
  width: number
  height: number
  allocatedBytes: number
  outputBytes: number
  cellsCompared: number
  rowsRasterized: number
  displayCommandsBuilt: number
  displayCommandsReused: number
  sink: "memory"
  heapDeltaBytes: number
}

function summarize(adapter: Result["adapter"], scenario: string, width: number, height: number, setupNs: number, samples: Sample[], heapDeltaBytes: number): Result {
  const sorted = samples.map(x => x.durationNs).sort((a, b) => a - b)
  return {
    schema: "hpd.tui.framework-comparison.v1", adapter,
    scenario,
    setupNs,
    meanNs: sorted.reduce((a, b) => a + b, 0) / sorted.length,
    medianNs: sorted[Math.floor(sorted.length * 0.5)]!,
    p95Ns: sorted[Math.min(sorted.length - 1, Math.ceil(sorted.length * 0.95) - 1)]!,
    width, height, allocatedBytes: heapDeltaBytes,
    outputBytes: samples.reduce((n, x) => n + x.outputBytes, 0),
    cellsCompared: 0, rowsRasterized: 0, displayCommandsBuilt: 0, displayCommandsReused: 0,
    sink: "memory", heapDeltaBytes,
  }
}

async function measure(adapter: Result["adapter"], scenario: string, width: number, height: number, setupNs: number, mutate: (i: number) => void, render: () => Promise<void>, captureBytes: () => number) {
  for (let i = 0; i < warmup; i++) { mutate(i); await render() }
  Bun.gc(true)
  const before = process.memoryUsage().heapUsed
  const samples: Sample[] = []
  for (let i = 0; i < iterations; i++) {
    mutate(i)
    const start = Bun.nanoseconds()
    await render()
    samples.push({ durationNs: Bun.nanoseconds() - start, outputBytes: captureBytes() })
  }
  Bun.gc(true)
  return summarize(adapter, scenario, width, height, setupNs, samples, process.memoryUsage().heapUsed - before)
}

class ByteSink extends Writable {
  readonly isTTY = true; columns: number; rows: number; private bytes = 0
  constructor(width: number, height: number) { super(); this.columns = width; this.rows = height }
  override _write(chunk: any, _encoding: BufferEncoding, callback: (error?: Error | null) => void) { this.bytes += Buffer.byteLength(chunk); callback() }
  take() { const value = this.bytes; this.bytes = 0; return value }
  getColorDepth() { return 24 }
}

async function runCore(width: number, height: number) {
  const setupStart = Bun.nanoseconds()
  const stdout = new ByteSink(width, height)
  const setup = await testing.createTestRenderer({ width, height, stdout: stdout as any, bufferedOutput: "stdout", useThread: false, maxFps: Number.POSITIVE_INFINITY })
  const row0 = new core.TextRenderable(setup.renderer, { content: "alpha", position: "absolute", top: 0, left: 0 })
  const row39 = new core.TextRenderable(setup.renderer, { content: "omega", position: "absolute", top: height - 1, left: 0 })
  setup.renderer.root.add(row0); setup.renderer.root.add(row39)
  const setupNs = Bun.nanoseconds() - setupStart
  const results = []
  const m = (name: string, mutate: (i: number) => void) => measure("opentui-core", name, width, height, setupNs, mutate, setup.renderOnce, () => stdout.take())
  results.push(await m("warm-noop", () => {}))
  results.push(await m("one-cell", i => { row0.content = i % 2 ? "alpha" : "Alpha" }))
  results.push(await m("one-row", i => { row0.content = (i % 2 ? "x" : "X").repeat(width) }))
  results.push(await m("two-disjoint-rows", i => { row0.content = i % 2 ? "alpha" : "Alpha"; row39.content = i % 2 ? "omega" : "Omega" }))
  results.push(await m("full-screen", i => { row0.content = ((i % 2 ? "x" : "X").repeat(width) + "\n").repeat(height) }))
  results.push(await m("cursor-only", i => { setup.renderer.setCursorPosition(i % width, 0, true) }))
  results.push(await m("style-only", i => { row0.fg = i % 2 ? "#ff0000" : "#00ff00" }))
  results.push(await m("wide-grapheme", i => { row0.content = i % 2 ? "界" : "語" }))
  results.push(await m("hyperlink", i => { row0.content = i % 2 ? "https://a.example" : "https://b.example" }))
  results.push(await m("resize", i => setup.resize(i % 2 ? width : Math.max(40, width - 1), i % 2 ? height : Math.max(12, height - 1))))
  setup.renderer.destroy()
  return results
}

async function runReact(width: number, height: number) {
  const setupStart = Bun.nanoseconds()
  const stdout = new ByteSink(width, height)
  const setup = await testing.createTestRenderer({ width, height, stdout: stdout as any, bufferedOutput: "stdout", useThread: false, maxFps: Number.POSITIVE_INFINITY })
  const root = reactHost.createRoot(setup.renderer)
  const renderTree = (first: string, last: string) => root.render(react.createElement(react.Fragment, null,
    react.createElement("text", { position: "absolute", top: 0, left: 0 }, first),
    react.createElement("text", { position: "absolute", top: height - 1, left: 0 }, last)))
  renderTree("alpha", "omega"); await setup.renderOnce()
  const setupNs = Bun.nanoseconds() - setupStart
  const results = []
  const m = (name: string, mutate: (i: number) => void) => measure("opentui-react", name, width, height, setupNs, mutate, setup.renderOnce, () => stdout.take())
  results.push(await m("warm-noop", () => renderTree("alpha", "omega")))
  results.push(await m("one-cell", i => renderTree(i % 2 ? "alpha" : "Alpha", "omega")))
  results.push(await m("one-row", i => renderTree((i % 2 ? "x" : "X").repeat(width), "omega")))
  results.push(await m("two-disjoint-rows", i => renderTree(i % 2 ? "alpha" : "Alpha", i % 2 ? "omega" : "Omega")))
  results.push(await m("full-screen", i => renderTree(((i % 2 ? "x" : "X").repeat(width) + "\n").repeat(height), "")))
  results.push(await m("cursor-only", i => renderTree(`cursor-${i % 2}`, "omega")))
  results.push(await m("style-only", i => root.render(react.createElement("text", { fg: i % 2 ? "#ff0000" : "#00ff00" }, "alpha"))))
  results.push(await m("wide-grapheme", i => renderTree(i % 2 ? "界" : "語", "omega")))
  results.push(await m("hyperlink", i => renderTree(i % 2 ? "https://a.example" : "https://b.example", "omega")))
  results.push(await m("resize", i => setup.resize(i % 2 ? width : Math.max(40, width - 1), i % 2 ? height : Math.max(12, height - 1))))
  root.unmount(); setup.renderer.destroy()
  return results
}

const dimensions = [[80, 24], [120, 40], [240, 80]] as const
const results = (await Promise.all(dimensions.flatMap(([w, h]) => [runCore(w, h), runReact(w, h)]))).flat()
console.log(JSON.stringify({
  schema: "hpd.tui.framework-comparison.v1",
  runtime: Bun.version,
  opentuiCommit: Bun.spawnSync(["git", "-C", reference, "rev-parse", "HEAD"]).stdout.toString().trim(),
  dimensions: dimensions.map(([width, height]) => ({ width, height })),
  warmup,
  iterations,
  note: "outputBytes is the UTF-8 size of ANSI/control bytes emitted through OpenTUI's native stdout feed.",
  results,
}, null, 2))
