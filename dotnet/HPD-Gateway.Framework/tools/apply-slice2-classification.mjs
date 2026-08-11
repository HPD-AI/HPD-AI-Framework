import { readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const classification = JSON.parse(readFileSync(resolve(
  root,
  '../../../../../../HPD-Agent-InternalDocs/HPD.Gateway/implementation/0016-public-type-classification.json'
), 'utf8'));
const folders = new Map([
  ['HPD.Gateway.Abstractions', 'Abstractions'],
  ['HPD.Gateway.Core', 'Core'],
  ['HPD.Gateway.Hosting', 'Hosting'],
  ['HPD.Gateway.Yarp', 'Yarp'],
  ['HPD.Gateway.Effective', 'Effective'],
  ['HPD.Gateway.Inspection', 'Inspection'],
  ['HPD.Gateway.Status', 'Status'],
  ['HPD.Gateway.OutputCaching', 'OutputCaching'],
  ['HPD.Gateway.Resilience', 'Resilience']
]);

let changed = 0;
for (const record of classification.records.filter((candidate) =>
  candidate.finalAccessibility === 'Internal' && folders.has(candidate.currentAssembly))) {
  const folder = join(root, 'src', 'HPD.Gateway', 'Embedded', folders.get(record.currentAssembly));
  const simpleName = record.currentType.split('+').at(-1).split('.').at(-1).replace(/`\d+$/, '');
  const escaped = simpleName.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const declaration = new RegExp(
    `\\bpublic(\\s+(?:(?:sealed|abstract|static|readonly|partial|ref)\\s+)*(?:class|record(?:\\s+struct)?|struct|interface|enum)\\s+${escaped}\\b)`
  );
  const delegate = new RegExp(`\\bpublic(\\s+delegate\\s+[^;{}]+?\\s+${escaped}\\s*\\()`);
  const matches = [];
  for (const file of files(folder)) {
    const text = readFileSync(file, 'utf8');
    if (declaration.test(text) || delegate.test(text))
      matches.push([file, text]);
  }
  if (matches.length !== 1)
    throw new Error(`Expected one declaration for ${record.currentType}; found ${matches.length}.`);
  const [file, text] = matches[0];
  const next = declaration.test(text)
    ? text.replace(declaration, 'internal$1')
    : text.replace(delegate, 'internal$1');
  if (next === text)
    throw new Error(`Failed to internalize ${record.currentType}.`);
  writeFileSync(file, next);
  changed++;
}

console.log(`Applied ${changed} embedded-runtime internal classifications.`);

function* files(directory) {
  for (const name of readdirSync(directory).sort(compareOrdinal)) {
    const path = join(directory, name);
    if (statSync(path).isDirectory()) yield* files(path);
    else if (path.endsWith('.cs')) yield path;
  }
}

function compareOrdinal(left, right) {
  return left < right ? -1 : left > right ? 1 : 0;
}
