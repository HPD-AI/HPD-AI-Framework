import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { basename, extname, join, relative, resolve, sep } from "node:path";

const gatewayRoot = resolve(import.meta.dirname, "..");
const cloudRoot = resolve(gatewayRoot, "../../../../../../HPD-Cloud");
const docsRoot = resolve(gatewayRoot, "../../../../../../../Documents/HPD Gateway");

const expectedProjects = [
  "HPD.Gateway",
  "HPD.Gateway.Admission.Redis",
  "HPD.Gateway.ControlPlane",
  "HPD.Gateway.ControlPlane.HPDAuth",
  "HPD.Gateway.ControlPlane.Sqlite",
  "HPD.Gateway.Discovery.Microsoft",
  "HPD.Gateway.Standalone"
];

const actualProjects = readdirSync(join(gatewayRoot, "src"), { withFileTypes: true })
  .filter(entry => entry.isDirectory() && existsSync(join(gatewayRoot, "src", entry.name, `${entry.name}.csproj`)))
  .map(entry => entry.name)
  .sort(ordinal);
assertEqual(actualProjects, expectedProjects, "Gateway product-project inventory");

const obsoleteNamespaces = [
  "Abstractions", "Admin", "Core", "Effective", "Hosting", "HPDAuth",
  "Inspection", "Management", "OutputCaching", "Resilience", "Status",
  "Studio", "Yarp"
];
const obsoleteCompositionMethods = [
  "AddHpdGatewayAdmin",
  "AddHpdGatewayAdminHpdAuth",
  "AddHpdGatewayManagement",
  "AddHpdGatewayStudio",
  "AddCoreFamilies",
  "AddStandardFeatures",
  "AddMicrosoftServiceDiscovery",
  "MapHpdGatewayAdmin",
  "MapHpdGatewayStudio"
].sort(ordinal);
const namespacePattern = new RegExp(
  `(?:using|namespace)\\s+HPD\\.Gateway\\.(?:${obsoleteNamespaces.join("|")})(?:[.;\\s])`);
const obsoleteApiPattern = new RegExp(`\\b(?:${obsoleteCompositionMethods.join("|")})\\b`);
const obsoleteProjectPattern = new RegExp(
  `(?:ProjectReference|PackageReference)[^>]+(?:HPD\\.Gateway\\.(?:${obsoleteNamespaces.join("|")}))`, "i");

const activeRoots = [
  join(gatewayRoot, "src"),
  join(gatewayRoot, "test", "HPD.Gateway.Tests"),
  join(gatewayRoot, "test", "HPD.Gateway.AotSmoke"),
  join(gatewayRoot, "test", "HPD.Gateway.Standalone.E2E"),
  resolve(gatewayRoot, "../../.github", "workflows"),
  join(cloudRoot, "src"),
  join(cloudRoot, "test"),
  docsRoot
];

const failures = [];
validateAdversarialFixtures();
for (const root of activeRoots) {
  if (!existsSync(root)) throw new Error(`Required migration root is absent: ${root}`);
  for (const file of files(root)) {
    const extension = extname(file);
    if (![".cs", ".csproj", ".md", ".json", ".js", ".mjs", ".ts", ".svelte", ".yml", ".yaml"].includes(extension)) continue;
    validateFile(file);
  }
}
validateFile(join(gatewayRoot, "README.md"));

if (failures.length !== 0) throw new Error(`Slice 5 migration validation failed:\n${failures.join("\n")}`);
console.log(`Slice 5 migration validation passed: ${expectedProjects.length} products; ${obsoleteCompositionMethods.length} deleted methods with fully qualified/documentation adversarial coverage; no obsolete active API, namespace, or package references.`);

function* files(directory) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    if (["bin", "obj", "node_modules", "dist", ".git", ".vitepress"].includes(entry.name)) continue;
    const path = join(directory, entry.name);
    if (entry.isDirectory()) yield* files(path);
    else if (entry.isFile() && statSync(path).size <= 16 * 1024 * 1024) yield path;
  }
}

function assertEqual(actual, expected, description) {
  if (actual.length !== expected.length || actual.some((value, index) => value !== expected[index])) {
    throw new Error(`${description} mismatch. Expected ${JSON.stringify(expected)}, received ${JSON.stringify(actual)}.`);
  }
}

function validateFile(file) {
  const text = readFileSync(file, "utf8");
  if (namespacePattern.test(text)) failures.push(`${display(file)}: obsolete namespace import/declaration`);
  if (obsoleteApiPattern.test(text)) failures.push(`${display(file)}: obsolete composition API`);
  if (obsoleteProjectPattern.test(text)) failures.push(`${display(file)}: obsolete project/package reference`);
}

function validateAdversarialFixtures() {
  const fixturePath = join(
    gatewayRoot,
    "test",
    "HPD.Gateway.ConsumerContracts",
    "MigrationValidator",
    "obsolete-api-cases.json");
  const fixture = JSON.parse(readFileSync(fixturePath, "utf8"));
  if (!Array.isArray(fixture) || fixture.length !== obsoleteCompositionMethods.length) {
    throw new Error("Obsolete API adversarial fixture must contain exactly one record per deleted method.");
  }
  const methods = fixture.map(entry => entry.method).sort(ordinal);
  assertEqual(methods, obsoleteCompositionMethods, "Obsolete composition-method fixture inventory");
  for (const entry of fixture) {
    if (typeof entry.csharp !== "string" || !entry.csharp.includes(`HPD.Gateway.`) || !obsoleteApiPattern.test(entry.csharp)) {
      throw new Error(`Fully qualified adversarial case is not rejected for ${entry.method}.`);
    }
    if (typeof entry.markdown !== "string" || !obsoleteApiPattern.test(entry.markdown)) {
      throw new Error(`Documentation adversarial case is not rejected for ${entry.method}.`);
    }
  }
  const allowed = "controlPlane.AddAdminApi().AddHpdAuth(\"gateway-admin\"); app.MapHpdGatewayControlPlane();";
  if (obsoleteApiPattern.test(allowed)) throw new Error("Final composition API produced an obsolete-method false positive.");
}

function ordinal(left, right) {
  return left < right ? -1 : left > right ? 1 : 0;
}

function display(path) {
  for (const [name, root] of [["gateway", gatewayRoot], ["cloud", cloudRoot], ["docs", docsRoot]]) {
    if (path === root || path.startsWith(root + sep)) return `${name}/${relative(root, path)}`;
  }
  return basename(path);
}
