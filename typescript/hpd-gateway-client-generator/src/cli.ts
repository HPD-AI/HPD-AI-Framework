#!/usr/bin/env node
import { emit, emitEditor } from "./emit.js";
import { loadSnapshot } from "./input.js";
import { createGenerationPlan } from "./normalize.js";
import { createEditorContract, loadEditorLedger } from "./editor.js";

async function main(args: readonly string[]): Promise<void> {
  if (args[0] !== "generate") usage();
  const snapshot = option(args, "--snapshot");
  const editorLedger = option(args, "--editor-ledger");
  const output = option(args, "--out");
  const known = new Set(["generate", "--snapshot", snapshot, "--editor-ledger", editorLedger, "--out", output, "--clean"]);
  if (args.some(value => !known.has(value))) usage();
  const parsedSnapshot = await loadSnapshot(snapshot);
  const files = await emit(createGenerationPlan(parsedSnapshot), output, args.includes("--clean"));
  const editorFiles = await emitEditor(createEditorContract(parsedSnapshot, await loadEditorLedger(editorLedger)), output);
  process.stdout.write(`Generated ${files.length + editorFiles.length} Gateway contract files in ${output}.\n`);
}

function option(args: readonly string[], name: string): string {
  const index = args.indexOf(name);
  const value = index < 0 ? undefined : args[index + 1];
  if (!value || value.startsWith("--")) usage();
  return value;
}

function usage(): never {
  process.stderr.write("Usage: hpd-gateway-client-generator generate --snapshot <file> --editor-ledger <file> --out <directory> [--clean]\n");
  process.exit(2);
}

main(process.argv.slice(2)).catch(error => {
  process.stderr.write(`Gateway generation failed: ${error instanceof Error ? error.message : "unknown error"}\n`);
  process.exitCode = 1;
});
