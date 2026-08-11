import { readFileSync, readdirSync, statSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const classification = JSON.parse(readFileSync(resolve(root,
  "../../../../../../HPD-Agent-InternalDocs/HPD.Gateway/implementation/0016-public-type-classification.json"), "utf8"));
const records = classification.records.filter((record) =>
  record.finalProduct === "HPD.Gateway.ControlPlane" &&
  record.disposition === "ImplementationInternal" &&
  !record.currentType.includes("+"));
const names = new Set(records.map((record) => record.currentType.split("+").at(-1).split(".").at(-1).replace(/`\d+$/, "")));
let changed = 0;

for (const file of files(join(root, "src/HPD.Gateway.ControlPlane"))) {
  const source = readFileSync(file, "utf8");
  let next = source;
  for (const name of names) {
    const escaped = name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const declaration = new RegExp(
      `\\bpublic(\\s+(?:(?:sealed|abstract|static|readonly|partial|ref)\\s+)*(?:class|record(?:\\s+struct)?|struct|interface|enum)\\s+${escaped}\\b)`, "g");
    const delegate = new RegExp(`\\bpublic(\\s+delegate\\s+[^;{}]+?\\s+${escaped}\\s*\\()`, "g");
    next = next.replace(declaration, (...match) => { changed++; return `internal${match[1]}`; });
    next = next.replace(delegate, (...match) => { changed++; return `internal${match[1]}`; });
  }
  if (next !== source) writeFileSync(file, next);
}

if (changed !== 0 && changed !== records.length)
  throw new Error(`Expected to internalize ${records.length} declarations; changed ${changed}.`);
console.log(changed === 0
  ? `All ${records.length} source-owned control-plane internal classifications are already applied.`
  : `Applied ${changed} source-owned control-plane internal classifications.`);

function* files(directory) {
  for (const name of readdirSync(directory).sort(compareOrdinal)) {
    const path = join(directory, name);
    if (statSync(path).isDirectory()) yield* files(path);
    else if (path.endsWith(".cs")) yield path;
  }
}

function compareOrdinal(left, right) {
  return left < right ? -1 : left > right ? 1 : 0;
}
