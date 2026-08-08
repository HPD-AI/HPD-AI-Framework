import { mkdir, rm, writeFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import type { GenerationPlan } from "./types.js";
import { render } from "./render.js";

export async function emit(plan: GenerationPlan, outputDirectory: string, clean = false): Promise<readonly string[]> {
  const root = resolve(outputDirectory);
  if (clean) await rm(root, { recursive: true, force: true });
  await mkdir(root, { recursive: true });
  const files = render(plan);
  const names = Object.keys(files).sort();
  for (const name of names) await writeFile(join(root, name), files[name]!, { encoding: "utf8", flag: "w" });
  return names;
}
