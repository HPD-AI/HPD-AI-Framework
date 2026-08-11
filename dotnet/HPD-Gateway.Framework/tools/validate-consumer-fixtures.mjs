import { readFileSync, statSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const fixtureRoot = join(root, 'test', 'HPD.Gateway.ConsumerContracts');
const manifest = JSON.parse(readFileSync(join(fixtureRoot, 'fixtures.json'), 'utf8'));
const productManifest = JSON.parse(readFileSync(resolve(
  root,
  '../../../../../../HPD-Agent-InternalDocs/HPD.Gateway/implementation/0016-product-manifests.json'
), 'utf8'));

if (manifest.fixtureVersion !== 'hpd-gateway-consumer-fixtures/v1' || manifest.targetFramework !== 'net10.0')
  throw new Error('Unsupported consumer-fixture contract.');

if (productManifest.manifestVersion !== 'hpd-gateway-product-manifests/v1')
  throw new Error('Unsupported product-manifest authority.');
const packableProducts = productManifest.products.filter((product) => product.packable);
const products = new Set(packableProducts.map((product) => product.packageId));
const namespaceOwners = new Map(packableProducts
  .map((product) => [product.rootNamespace, product.packageId])
  .sort(([left], [right]) => right.length - left.length || compareOrdinal(left, right)));
const ids = new Set();
let positive = 0;
let negative = 0;
let native = 0;

expectImportFailure(
  ['HPD.Gateway'],
  ['HPD.Gateway.ControlPlane.Sqlite'],
  "without declaring owning package 'HPD.Gateway.ControlPlane.Sqlite'"
);
expectImportFailure(
  ['HPD.Gateway', 'HPD.Gateway.ControlPlane'],
  ['HPD.Gateway'],
  "declares unused Gateway package(s): HPD.Gateway.ControlPlane"
);

for (const fixture of manifest.fixtures) {
  if (typeof fixture.id !== 'string' || !/^[a-z0-9-]+$/.test(fixture.id) || !ids.add(fixture.id))
    throw new Error(`Invalid or duplicate fixture ID '${fixture.id}'.`);
  if (fixture.expect !== 'success' && fixture.expect !== 'failure')
    throw new Error(`Fixture '${fixture.id}' has an invalid expectation.`);
  if (!Array.isArray(fixture.packages) || fixture.packages.some((value) => !products.has(value)))
    throw new Error(`Fixture '${fixture.id}' references a non-product package.`);
  if (new Set(fixture.packages).size !== fixture.packages.length)
    throw new Error(`Fixture '${fixture.id}' repeats a package.`);
  if (fixture.kind === 'native-aot-distribution') {
    native++;
    requireExactMembers(fixture, ['id', 'expect', 'kind', 'packages', 'project']);
    const expectedProject = join(root, 'src', 'HPD.Gateway.Standalone', 'HPD.Gateway.Standalone.csproj');
    const actualProject = typeof fixture.project === 'string' ? resolve(root, fixture.project) : '';
    if (fixture.expect !== 'success' || fixture.packages.length !== 0 || actualProject !== expectedProject)
      throw new Error(`Native fixture '${fixture.id}' is malformed.`);
    statSync(actualProject);
    continue;
  }
  if (typeof fixture.source !== 'string')
    throw new Error(`Fixture '${fixture.id}' has no source.`);
  const sourcePath = join(fixtureRoot, fixture.source);
  statSync(sourcePath);
  const source = readFileSync(sourcePath, 'utf8');
  if (/ProjectReference|HintPath|extern alias/.test(source))
    throw new Error(`Fixture '${fixture.id}' attempts to bypass package isolation.`);
  const namespaces = [...source.matchAll(/^using (HPD\.Gateway(?:\.[A-Za-z0-9_.]+)?);$/gm)].map((match) => match[1]);
  if (fixture.expect === 'success') {
    positive++;
    validateImports(fixture.id, fixture.packages, namespaces);
  } else {
    negative++;
    if (typeof fixture.diagnostic !== 'string' || !/^CS[0-9]{4}$/.test(fixture.diagnostic))
      throw new Error(`Negative fixture '${fixture.id}' has no exact compiler diagnostic.`);
  }
}

if (positive !== 11 || negative !== 4 || native !== 1 || manifest.fixtures.length !== 16)
  throw new Error(`Expected eleven positive, four negative, and one Native AOT fixture; found ${positive}, ${negative}, and ${native}.`);

console.log('Consumer fixture contract passed: 11 package-positive, 4 package-negative, 1 Native AOT distribution; undeclared-owner and unused-package adversarial cases rejected.');

function validateImports(id, packages, namespaces) {
  const usedPackages = new Set();
  for (const namespace of namespaces) {
    const owner = [...namespaceOwners]
      .find(([rootNamespace]) => namespace === rootNamespace || namespace.startsWith(`${rootNamespace}.`))?.[1];
    if (!owner)
      throw new Error(`Positive fixture '${id}' imports obsolete namespace '${namespace}'.`);
    if (!packages.includes(owner))
      throw new Error(`Positive fixture '${id}' imports '${namespace}' without declaring owning package '${owner}'.`);
    usedPackages.add(owner);
  }
  const unused = packages.filter((packageId) => !usedPackages.has(packageId));
  if (unused.length !== 0)
    throw new Error(`Positive fixture '${id}' declares unused Gateway package(s): ${unused.join(', ')}.`);
}

function expectImportFailure(packages, namespaces, expected) {
  try {
    validateImports('adversarial-self-test', packages, namespaces);
  } catch (error) {
    if (error instanceof Error && error.message.includes(expected))
      return;
    throw error;
  }
  throw new Error(`Consumer-fixture adversarial self-test did not reject: ${expected}.`);
}

function requireExactMembers(value, expected) {
  const actual = Object.keys(value).sort(compareOrdinal);
  const orderedExpected = [...expected].sort(compareOrdinal);
  if (actual.length !== orderedExpected.length || actual.some((member, index) => member !== orderedExpected[index]))
    throw new Error('Native AOT fixture members do not match the closed contract.');
}

function compareOrdinal(left, right) {
  return left < right ? -1 : left > right ? 1 : 0;
}
