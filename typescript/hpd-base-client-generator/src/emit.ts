import { mkdir, rm, stat, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { renderGeneratedFiles } from "./render.js";
import type { GenerationPlan } from "./types.js";

export async function writeGeneratedFiles(plan: GenerationPlan, outDir: string, clean = false): Promise<void> {
  if (await pathIsFile(outDir)) throw new Error(`Output path is a file: ${outDir}`);
  if (clean) await rm(outDir, { recursive: true, force: true });
  await mkdir(outDir, { recursive: true });
  for (const file of renderGeneratedFiles(plan)) {
    await writeFile(join(outDir, file.path), file.content, "utf8");
  }
}

async function pathIsFile(path: string): Promise<boolean> {
  try {
    return (await stat(path)).isFile();
  } catch {
    return false;
  }
}
