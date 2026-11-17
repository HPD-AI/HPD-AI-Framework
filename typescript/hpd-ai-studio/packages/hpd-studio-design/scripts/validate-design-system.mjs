import { readFile, readdir } from 'node:fs/promises';
import { extname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const packageRoot = fileURLToPath(new URL('..', import.meta.url));
const studioRoot = fileURLToPath(new URL('../../../', import.meta.url));
const designSources = join(packageRoot, 'src');
const moduleRoot = join(studioRoot, 'modules');
const shellSources = join(studioRoot, 'shell', 'src');
const sourceRegistry = join(shellSources, 'studio', 'styles', 'sources.css');

const cssFiles = await sourceFiles(designSources, new Set(['.css']));
const styleText = (await Promise.all(cssFiles.map(file => readFile(file, 'utf8')))).join('\n');
const definedVariables = new Set([...styleText.matchAll(/(--[a-z0-9-]+)\s*:/giu)].map(match => match[1]));
const definedUtilities = new Set([...styleText.matchAll(/@utility\s+(studio-[a-z0-9-]+)/giu)].map(match => match[1]));
const definedClasses = new Set([
  ...definedUtilities,
  ...[...styleText.matchAll(/\.(studio-[a-z0-9-]+)\b/giu)].map(match => match[1])
]);
const issues = [];

for (const file of cssFiles) {
  const text = await readFile(file, 'utf8');
  for (const match of text.matchAll(/var\(\s*(--[a-z0-9-]+)([^)]*)\)/giu)) {
    const [, variable, tail] = match;
    if (!definedVariables.has(variable) && !tail.trimStart().startsWith(','))
      issues.push(`${display(file)}: undefined variable ${variable} without a fallback`);
  }
}

const modules = (await directories(moduleRoot))
  .map(name => ({ name, source: join(moduleRoot, name, 'src') }));
const registeredSources = await readFile(sourceRegistry, 'utf8');
const scanRoots = [designSources, shellSources, ...modules.map(value => value.source)];
for (const root of scanRoots) {
  for (const file of await sourceFiles(root, new Set(['.svelte', '.css']))) {
    const text = await readFile(file, 'utf8');
    const style = text.match(/<style[^>]*>([\s\S]*?)<\/style>/iu)?.[1] ?? '';
    for (const match of style.matchAll(/\.(studio-[a-z0-9-]+)\b/giu)) definedClasses.add(match[1]);
    for (const match of text.matchAll(/(?<![\w-])studio-[a-z0-9-]+\b/giu)) {
      const name = match[0];
      if (name === 'studio-main' || name === 'studio-workbench') continue;
      if (!definedClasses.has(name))
        issues.push(`${display(file)}: referenced class ${name} has no @utility definition`);
    }
  }
}

for (const module of modules) {
  const expected = relative(join(shellSources, 'studio', 'styles'), module.source).replaceAll('\\', '/');
  if (!registeredSources.includes(`@source "${expected}"`))
    issues.push(`sources.css: installed module ${module.name} is not registered at ${expected}`);
  for (const file of await sourceFiles(module.source, new Set(['.svelte', '.css']))) {
    const text = await readFile(file, 'utf8');
    if (/#[0-9a-f]{3,8}\b|(?:oklch|rgba?|hsla?)\(/iu.test(text))
      issues.push(`${display(file)}: module source contains a raw color value; use a semantic token`);
  }
}

if (issues.length > 0) {
  console.error(`Studio design-system validation failed with ${issues.length} issue(s):`);
  for (const issue of [...new Set(issues)].sort()) console.error(`- ${issue}`);
  process.exitCode = 1;
} else {
  console.log(`Studio design-system validation passed (${definedVariables.size} variables, ${definedClasses.size} classes).`);
}

async function sourceFiles(root, extensions) {
  const result = [];
  for (const entry of await readdir(root, { withFileTypes: true })) {
    if (entry.name === 'node_modules' || entry.name === 'dist' || entry.name === '.svelte-check') continue;
    const path = join(root, entry.name);
    if (entry.isDirectory()) result.push(...await sourceFiles(path, extensions));
    else if (extensions.has(extname(entry.name))) result.push(path);
  }
  return result;
}

async function directories(root) {
  return (await readdir(root, { withFileTypes: true }))
    .filter(entry => entry.isDirectory())
    .map(entry => entry.name)
    .sort();
}

function display(file) {
  return relative(studioRoot, file).replaceAll('\\', '/');
}
