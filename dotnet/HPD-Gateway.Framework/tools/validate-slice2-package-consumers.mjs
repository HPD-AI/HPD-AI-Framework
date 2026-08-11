import { createHash } from "node:crypto";
import { mkdtempSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { spawnSync } from "node:child_process";

const root = resolve(import.meta.dirname, "..");
const feed = resolve(process.argv[2] ?? "");
if (!process.argv[2]) throw new Error("Usage: validate-slice2-package-consumers.mjs <package-feed>");
const packageFiles = readdirSync(feed).filter((name) => /^HPD\.Gateway\.(?!.*\.symbols\.nupkg$).+\.nupkg$/i.test(name));
if (packageFiles.length !== 1) throw new Error(`Expected exactly one HPD.Gateway nupkg in ${feed}.`);
const packageFile = packageFiles[0];
const versionMatch = /^HPD\.Gateway\.(.+)\.nupkg$/i.exec(packageFile);
if (!versionMatch) throw new Error("The produced package filename does not contain a version.");
const packageVersion = versionMatch[1];
const packageArtifact = join(feed, packageFile);
const expectedPackageHash = sha256(readFileSync(packageArtifact));
const expectedAssemblyHash = sha256(extractPackageAssembly(packageArtifact));
const fixtures = JSON.parse(readFileSync(join(root, "test/HPD.Gateway.ConsumerContracts/fixtures.json"), "utf8"));
const selected = fixtures.fixtures.filter((fixture) =>
  ["embedded-minimal", "embedded-core-declarations", "obsolete-namespace"].includes(fixture.id));
const work = mkdtempSync(join(tmpdir(), "hpd-gateway-slice2-consumers-"));
const packages = join(work, "packages");
const nugetConfig = join(work, "NuGet.Config");
mkdirSync(packages);
writeFileSync(nugetConfig, `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="slice2" value="${xml(feed)}" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="slice2"><package pattern="HPD.Gateway" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
  <config><add key="globalPackagesFolder" value="${xml(packages)}" /></config>
</configuration>
`);
const isolatedEnvironment = { ...process.env, NUGET_PACKAGES: packages };

try {
  for (const fixture of selected) {
    const directory = join(work, fixture.id);
    const source = readFileSync(join(root, "test/HPD.Gateway.ConsumerContracts", fixture.source), "utf8");
    mkdirSync(directory);
    writeFileSync(join(directory, "Program.cs"), source);
    writeFileSync(join(directory, "Consumer.csproj"), `<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="HPD.Gateway" Version="${packageVersion}" /></ItemGroup>
</Project>
`);
    const restore = spawn("dotnet", ["restore", "Consumer.csproj", "--nologo", "--no-cache", "--force-evaluate",
      "--configfile", nugetConfig], directory, true, isolatedEnvironment);
    if (restore.status !== 0) throw new Error(`${fixture.id} restore failed.\n${restore.output}`);
    verifyResolvedArtifact(directory);
    const result = spawn("dotnet", ["build", "Consumer.csproj", "-c", "Release", "--nologo", "--no-restore"],
      directory, true, isolatedEnvironment);
    if (fixture.expect === "success" && result.status !== 0)
      throw new Error(`${fixture.id} did not compile from the produced package.\n${result.output}`);
    if (fixture.expect === "failure") {
      if (result.status === 0 || !result.output.includes(fixture.diagnostic))
        throw new Error(`${fixture.id} did not fail with ${fixture.diagnostic}.\n${result.output}`);
    }
    process.stdout.write(`${fixture.id}: exact package ${expectedPackageHash.slice(0, 12)}…; ${
      fixture.expect === "success" ? "compiled" : `rejected with ${fixture.diagnostic}`}\n`);
  }
} finally {
  rmSync(work, { recursive: true, force: true });
}

function verifyResolvedArtifact(directory) {
  const assets = JSON.parse(readFileSync(join(directory, "obj/project.assets.json"), "utf8"));
  const packageFolders = Object.keys(assets.packageFolders ?? {}).map((path) => resolve(path));
  if (packageFolders.length !== 1 || packageFolders[0] !== resolve(packages))
    throw new Error(`Restore escaped the isolated package directory: ${packageFolders.join(", ")}.`);
  if (!Object.hasOwn(assets.libraries ?? {}, `HPD.Gateway/${packageVersion}`))
    throw new Error("project.assets.json does not contain the produced HPD.Gateway identity.");
  const resolved = join(packages, "hpd.gateway", packageVersion.toLowerCase());
  const cachedPackage = join(resolved, `hpd.gateway.${packageVersion.toLowerCase()}.nupkg`);
  const cachedAssembly = join(resolved, "lib/net10.0/HPD.Gateway.dll");
  if (sha256(readFileSync(cachedPackage)) !== expectedPackageHash)
    throw new Error("Resolved HPD.Gateway nupkg hash differs from the supplied artifact.");
  if (sha256(readFileSync(cachedAssembly)) !== expectedAssemblyHash)
    throw new Error("Resolved HPD.Gateway assembly hash differs from the supplied artifact.");
}

function extractPackageAssembly(packagePath) {
  const result = spawnSync("unzip", ["-p", packagePath, "lib/net10.0/HPD.Gateway.dll"],
    { encoding: null, maxBuffer: 16 * 1024 * 1024 });
  if (result.status !== 0 || result.stdout.length === 0)
    throw new Error("Could not read HPD.Gateway.dll from the produced package.");
  return result.stdout;
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

function xml(value) {
  return value.replaceAll("&", "&amp;").replaceAll('"', "&quot;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");
}

function spawn(command, args, cwd = root, capture = false, env = process.env) {
  const result = spawnSync(command, args, { cwd, env, encoding: "utf8", stdio: capture ? "pipe" : "inherit" });
  result.output = `${result.stdout ?? ""}${result.stderr ?? ""}`;
  if (!capture && result.status !== 0) throw new Error(`${command} failed with exit code ${result.status}.`);
  return result;
}
