import { existsSync, readdirSync } from "node:fs";
import { basename, join, resolve } from "node:path";
import { spawnSync } from "node:child_process";

const feed = resolve(process.argv[2] ?? "");
const argumentsByName = parseArguments(process.argv.slice(3));
const standalone = argumentsByName.get("standalone") ? resolve(argumentsByName.get("standalone")) : null;
if (!process.argv[2])
  throw new Error("Usage: validate-slice6-release.mjs <package-feed> --gateway-version <version> --base-version <version> --auth-version <version> --platform-version <version> [--standalone <publish-directory>]");

const gatewayVersion = requireArgument("gateway-version");
const baseVersion = requireArgument("base-version");
const authVersion = requireArgument("auth-version");
const platformVersion = requireArgument("platform-version");

const products = new Map([
  ["HPD.Gateway", new Map([["Microsoft.Extensions.Http.Resilience", "10.0.0"], ["Yarp.ReverseProxy", "2.3.0"]])],
  ["HPD.Gateway.ControlPlane", new Map([["HPD-AI.Platform", platformVersion], ["HPD.Base", baseVersion], ["HPD.Gateway", gatewayVersion], ["Microsoft.AspNetCore.OpenApi", "10.0.10"], ["Microsoft.OpenApi", "2.11.0"]])],
  ["HPD.Gateway.ControlPlane.HPDAuth", new Map([["HPD-Auth-ControlPlane-AspNetCore", authVersion], ["HPD-Auth-Core", authVersion], ["HPD.Gateway.ControlPlane", gatewayVersion]])],
  ["HPD.Gateway.ControlPlane.Sqlite", new Map([["HPD.Base.Sqlite", baseVersion], ["HPD.Gateway.ControlPlane", gatewayVersion]])],
  ["HPD.Gateway.Discovery.Microsoft", new Map([["HPD.Gateway", gatewayVersion], ["Microsoft.Extensions.ServiceDiscovery", "10.7.0"], ["Microsoft.Extensions.ServiceDiscovery.Dns", "10.7.0"]])],
  ["HPD.Gateway.Admission.Redis", new Map([["HPD.Gateway", gatewayVersion], ["StackExchange.Redis", "3.0.17"]])],
]);
const packages = readdirSync(feed)
  .filter(name => /^HPD\.Gateway(?:\.|-)[^.]*.*\.nupkg$/i.test(name) && !name.endsWith(".snupkg"));
if (packages.length !== products.size)
  throw new Error(`Expected exactly ${products.size} Gateway nupkgs; found ${packages.length}.`);

for (const [id, expectedDependencies] of products) {
  const escaped = id.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const matches = packages.filter(name => new RegExp(`^${escaped}\\.[0-9].*\\.nupkg$`, "i").test(name));
  if (matches.length !== 1) throw new Error(`Expected exactly one ${id} artifact.`);
  const packagePath = join(feed, matches[0]);
  const entries = unzip(packagePath, ["-l"]).split(/\r?\n/)
    .map(line => line.trim().split(/\s+/).at(-1))
    .filter(value => value && !value.endsWith("/") && !/^[-]+$/.test(value));
  const assembly = `lib/net10.0/${id}.dll`;
  const documentation = `lib/net10.0/${id}.xml`;
  if (!entries.includes(assembly) || !entries.includes(documentation))
    throw new Error(`${id} is missing its exact assembly or XML documentation.`);
  if (entries.some(entry => /HPD\.Gateway\.(?:Abstractions|Admin|Core|Effective|Hosting|HPDAuth|Inspection|Management|OutputCaching|Resilience|Status|Studio|Yarp)\.dll$/i.test(entry)))
    throw new Error(`${id} contains an obsolete Gateway assembly.`);
  if (entries.some(entry => entry.includes("/test/") || entry.includes("/node_modules/") || entry.endsWith(".map")))
    throw new Error(`${id} contains development-only Studio content.`);
  if (id === "HPD.Gateway.ControlPlane" && !entries.some(entry => entry.startsWith("clientapp-modules/hpd-gateway-studio/src/")))
    throw new Error("The control-plane package omitted its governed Studio module sources.");

  const nuspec = unzip(packagePath, ["-p", `${id}.nuspec`]);
  const dependencies = new Map([...nuspec.matchAll(/<dependency\s+id="([^"]+)"\s+version="([^"]+)"/g)]
    .map(match => [match[1], match[2]]));
  assertEqual([...dependencies.keys()].sort(ordinal), [...expectedDependencies.keys()].sort(ordinal), `${id} dependency graph`);
  for (const [dependencyId, expectedVersion] of expectedDependencies)
    assertDependencyVersion(id, dependencyId, dependencies.get(dependencyId), expectedVersion);
  process.stdout.write(`${basename(packagePath)}: payload and dependency graph accepted.\n`);
}

if (standalone !== null) {
  const executable = join(standalone, process.platform === "win32" ? "HPD.Gateway.Standalone.exe" : "HPD.Gateway.Standalone");
  if (!existsSync(executable)) throw new Error("Standalone executable distribution is missing its entry point.");
  const standaloneFiles = readdirSync(standalone);
  if (standaloneFiles.some(name => /^HPD\.Gateway\.(?:Abstractions|Admin|Core|Effective|Hosting|HPDAuth|Inspection|Management|OutputCaching|Resilience|Status|Studio|Yarp)\.dll$/i.test(name)))
    throw new Error("Standalone distribution contains an obsolete Gateway assembly.");
  if (standaloneFiles.some(name => /^HPD\.Gateway(?:\..+)?\.nupkg$/i.test(name)))
    throw new Error("Standalone executable distribution must not contain library packages.");
  process.stdout.write(`Slice 6 release artifacts accepted: ${products.size} packages and one Standalone executable distribution.\n`);
} else {
  process.stdout.write(`Slice 6 package artifacts accepted: exactly ${products.size} packages.\n`);
}

function unzip(path, args) {
  const result = spawnSync("unzip", [args[0], path, ...args.slice(1)], { encoding: "utf8", maxBuffer: 32 * 1024 * 1024 });
  if (result.status !== 0) throw new Error(`Could not inspect ${path}: ${result.stderr}`);
  return result.stdout;
}
function ordinal(left, right) { return left < right ? -1 : left > right ? 1 : 0; }
function parseArguments(values) {
  if (values.length % 2 !== 0) throw new Error("Every release-validator option requires one value.");
  const parsed = new Map();
  for (let index = 0; index < values.length; index += 2) {
    const name = values[index];
    if (!name.startsWith("--") || parsed.has(name.slice(2))) throw new Error(`Invalid or duplicate option '${name}'.`);
    parsed.set(name.slice(2), values[index + 1]);
  }
  return parsed;
}
function requireArgument(name) {
  const value = argumentsByName.get(name);
  if (!value || !/^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$/.test(value))
    throw new Error(`--${name} must be a three-part NuGet version with an optional prerelease suffix.`);
  return value;
}
function assertDependencyVersion(productId, dependencyId, actual, expected) {
  if (actual !== expected && actual !== `[${expected}, )`)
    throw new Error(`${productId} dependency ${dependencyId} must use ${expected}; got ${actual ?? "missing"}.`);
}
function assertEqual(actual, expected, label) {
  if (actual.length !== expected.length || actual.some((value, index) => value !== expected[index]))
    throw new Error(`${label} differs. Expected ${expected.join(", ")}; got ${actual.join(", ")}.`);
}
