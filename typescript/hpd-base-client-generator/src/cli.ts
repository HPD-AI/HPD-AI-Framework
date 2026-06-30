#!/usr/bin/env node
import { loadGeneratorConfig, loadSnapshot } from "./input.js";
import { createGenerationPlan } from "./normalize.js";
import { writeGeneratedFiles } from "./emit.js";
import type { GenerateOptions } from "./types.js";

export async function main(argv = process.argv.slice(2)): Promise<void> {
  const { command, options } = parseArgs(argv);
  if (command !== "generate") throw new Error("Expected command: generate");
  if (!options.out) throw new Error("Missing required --out <dir>.");
  const snapshot = await loadSnapshot(options);
  const config = await loadGeneratorConfig(options.config, options.banner);
  const plan = createGenerationPlan(snapshot, config);
  for (const warning of plan.warnings) console.warn(`warning: ${warning}`);
  await writeGeneratedFiles(plan, options.out, options.clean);
}

function parseArgs(argv: string[]): { command: string | undefined; options: GenerateOptions } {
  const [command, ...rest] = argv;
  const options: Partial<GenerateOptions> = {};
  for (let index = 0; index < rest.length; index += 1) {
    const arg = rest[index];
    if (arg === "--clean") {
      options.clean = true;
      continue;
    }
    if (!arg.startsWith("--")) throw new Error(`Unexpected argument ${arg}.`);
    const key = arg.slice(2) as keyof GenerateOptions;
    const value = rest[index + 1];
    if (!value || value.startsWith("--")) throw new Error(`Missing value for ${arg}.`);
    (options as Record<string, string>)[key] = value;
    index += 1;
  }
  return { command, options: options as GenerateOptions };
}

if (import.meta.url === `file://${process.argv[1]}`) {
  main().catch(error => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  });
}
