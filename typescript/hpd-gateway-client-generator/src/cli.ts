#!/usr/bin/env node
import { emit } from "./emit.js";
import { loadSnapshot } from "./input.js";
import { createGenerationPlan } from "./normalize.js";

async function main(args: readonly string[]): Promise<void> {
  if (args[0] !== "generate") usage();
  const snapshot = option(args, "--snapshot");
  const output = option(args, "--out");
  const known = new Set(["generate", "--snapshot", snapshot, "--out", output, "--clean"]);
  if (args.some(value => !known.has(value))) usage();
  const files = await emit(createGenerationPlan(await loadSnapshot(snapshot)), output, args.includes("--clean"));
  process.stdout.write(`Generated ${files.length} Gateway contract files in ${output}.\n`);
}

function option(args: readonly string[], name: string): string {
  const index = args.indexOf(name);
  const value = index < 0 ? undefined : args[index + 1];
  if (!value || value.startsWith("--")) usage();
  return value;
}

function usage(): never {
  process.stderr.write("Usage: hpd-gateway-client-generator generate --snapshot <file> --out <directory> [--clean]\n");
  process.exit(2);
}

main(process.argv.slice(2)).catch(error => {
  process.stderr.write(`Gateway generation failed: ${error instanceof Error ? error.message : "unknown error"}\n`);
  process.exitCode = 1;
});
