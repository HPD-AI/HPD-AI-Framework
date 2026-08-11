import { createHash } from "node:crypto";
import { mkdtempSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { spawnSync } from "node:child_process";

const root = resolve(import.meta.dirname, "..");
const feed = resolve(process.argv[2] ?? "");
const boundary = process.argv[3] ?? "slice3";
if (!process.argv[2] || !["slice3", "slice4", "slice4-all", "slice5-redis", "slice6"].includes(boundary))
  throw new Error("Usage: validate-slice3-package-consumers.mjs <package-feed> [slice3|slice4|slice4-all|slice5-redis|slice6]");
const verifiedIds = boundary === "slice6"
  ? ["HPD.Gateway", "HPD.Gateway.ControlPlane", "HPD.Gateway.ControlPlane.Sqlite",
      "HPD.Gateway.ControlPlane.HPDAuth", "HPD.Gateway.Discovery.Microsoft", "HPD.Gateway.Admission.Redis"]
  : boundary === "slice4-all"
  ? ["HPD.Gateway", "HPD.Gateway.ControlPlane", "HPD.Gateway.ControlPlane.Sqlite",
      "HPD.Gateway.ControlPlane.HPDAuth", "HPD.Gateway.Discovery.Microsoft"]
  : boundary === "slice5-redis"
    ? ["HPD.Gateway", "HPD.Gateway.Admission.Redis"]
  : boundary === "slice4"
    ? ["HPD.Gateway", "HPD.Gateway.ControlPlane", "HPD.Gateway.ControlPlane.Sqlite"]
    : ["HPD.Gateway", "HPD.Gateway.ControlPlane"];
const verifiedPackages = new Map(verifiedIds.map(artifact));
const fixtures = JSON.parse(readFileSync(join(root, "test/HPD.Gateway.ConsumerContracts/fixtures.json"), "utf8"));
const selectedIds = boundary === "slice5-redis"
  ? ["redis-admission", "redis-admission-host-owned", "redis-provider-from-root-only"]
  : boundary === "slice4-all"
  ? ["control-plane-sqlite", "hpd-auth-security", "microsoft-configuration-discovery",
      "microsoft-dns-discovery", "full-cloud-equivalent", "obsolete-hpd-auth-namespace",
      "obsolete-microsoft-discovery-method"]
  : boundary === "slice4"
    ? ["control-plane-sqlite"]
    : ["control-plane-process-local", "control-plane-studio", "plain-aspnet-security", "reverse-control-plane-dependency"];
const selected = boundary === "slice6"
  ? fixtures.fixtures.filter((fixture) => typeof fixture.source === "string")
  : fixtures.fixtures.filter((fixture) => selectedIds.includes(fixture.id));
const work = mkdtempSync(join(tmpdir(), `hpd-gateway-${boundary}-consumers-`));
const packages = join(work, "packages");
const nugetConfig = join(work, "NuGet.Config");
mkdirSync(packages);
writeFileSync(nugetConfig, `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="slice3" value="${xml(feed)}" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping>
    <packageSource key="slice3">
      <package pattern="HPD.Gateway" /><package pattern="HPD.Gateway.ControlPlane" />
      <package pattern="HPD.Gateway.ControlPlane.Sqlite" />
      <package pattern="HPD.Gateway.ControlPlane.HPDAuth" /><package pattern="HPD.Gateway.Discovery.Microsoft" />
      <package pattern="HPD.Gateway.Admission.Redis" />
      <package pattern="HPD.Base" /><package pattern="HPD.Base.Sqlite" /><package pattern="HPD-Events" /><package pattern="HPD-AI.Platform" />
      <package pattern="HPD-Auth-*" />
    </packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
  <config><add key="globalPackagesFolder" value="${xml(packages)}" /></config>
</configuration>
`);
const environment = { ...process.env, NUGET_PACKAGES: packages };

try {
  for (const fixture of selected) {
    const directory = join(work, fixture.id);
    mkdirSync(directory);
    writeFileSync(join(directory, "Program.cs"), readFileSync(
      join(root, "test/HPD.Gateway.ConsumerContracts", fixture.source), "utf8"));
    const references = fixture.packages.map((id) => {
      const value = verifiedPackages.get(id) ?? artifact(id)[1];
      return `    <PackageReference Include="${id}" Version="${value.version}" />`;
    }).join("\n");
    writeFileSync(join(directory, "Consumer.csproj"), `<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
  <ItemGroup>\n${references}\n  </ItemGroup>
</Project>
`);
    const restore = run("dotnet", ["restore", "Consumer.csproj", "--nologo", "--no-cache", "--force-evaluate", "--configfile", nugetConfig], directory);
    if (restore.status !== 0) throw new Error(`${fixture.id} restore failed.\n${restore.output}`);
    verifyAssets(directory, fixture.packages);
    const build = run("dotnet", ["build", "Consumer.csproj", "-c", "Release", "--nologo", "--no-restore"], directory);
    if (fixture.expect === "success" && build.status !== 0)
      throw new Error(`${fixture.id} did not compile.\n${build.output}`);
    if (fixture.expect === "failure" && (build.status === 0 || !build.output.includes(fixture.diagnostic)))
      throw new Error(`${fixture.id} did not fail with ${fixture.diagnostic}.\n${build.output}`);
    if (fixture.execute === true) {
      const execution = run("dotnet", ["run", "--project", "Consumer.csproj", "-c", "Release", "--no-build", "--no-restore"], directory);
      if (execution.status !== 0 || !execution.output.includes("package-only SQLite startup and restart reconciliation passed"))
        throw new Error(`${fixture.id} did not execute the governed restart lifecycle.\n${execution.output}`);
    }
    process.stdout.write(`${fixture.id}: exact local artifacts verified; ${fixture.expect === "success" ? "compiled" : `rejected with ${fixture.diagnostic}`}\n`);
  }
} finally {
  rmSync(work, { recursive: true, force: true });
}

function artifact(id) {
  const escaped = id.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const files = readdirSync(feed).filter((name) => new RegExp(`^${escaped}\\.[0-9][0-9A-Za-z.+-]*\\.nupkg$`, "i").test(name));
  if (files.length !== 1) throw new Error(`Expected exactly one ${id} nupkg in ${feed}.`);
  const version = new RegExp(`^${escaped}\\.(.+)\\.nupkg$`, "i").exec(files[0])[1];
  const packagePath = join(feed, files[0]);
  const assemblyPath = `lib/net10.0/${id}.dll`;
  return [id, {
    id, version,
    packageHash: sha256(readFileSync(packagePath)),
    assemblyHash: sha256(extract(packagePath, assemblyPath)),
  }];
}

function verifyAssets(directory, declaredPackages) {
  const assets = JSON.parse(readFileSync(join(directory, "obj/project.assets.json"), "utf8"));
  const roots = Object.keys(assets.packageFolders ?? {}).map((path) => resolve(path));
  if (roots.length !== 1 || roots[0] !== resolve(packages)) throw new Error("Restore escaped the isolated package directory.");
  for (const id of declaredPackages) {
    const expected = verifiedPackages.get(id);
    if (!expected) continue;
    if (!Object.hasOwn(assets.libraries ?? {}, `${id}/${expected.version}`)) throw new Error(`${id} identity is absent from project.assets.json.`);
    const directoryName = join(packages, id.toLowerCase(), expected.version.toLowerCase());
    if (sha256(readFileSync(join(directoryName, `${id.toLowerCase()}.${expected.version.toLowerCase()}.nupkg`))) !== expected.packageHash)
      throw new Error(`${id} package hash differs from the supplied artifact.`);
    if (sha256(readFileSync(join(directoryName, `lib/net10.0/${id}.dll`))) !== expected.assemblyHash)
      throw new Error(`${id} assembly hash differs from the supplied artifact.`);
  }
}

function extract(packagePath, member) {
  const result = spawnSync("unzip", ["-p", packagePath, member], { encoding: null, maxBuffer: 16 * 1024 * 1024 });
  if (result.status !== 0 || result.stdout.length === 0) throw new Error(`Could not read ${member} from ${packagePath}.`);
  return result.stdout;
}
function sha256(bytes) { return createHash("sha256").update(bytes).digest("hex"); }
function xml(value) { return value.replaceAll("&", "&amp;").replaceAll('"', "&quot;").replaceAll("<", "&lt;").replaceAll(">", "&gt;"); }
function run(command, args, cwd) {
  const result = spawnSync(command, args, { cwd, env: environment, encoding: "utf8", stdio: "pipe" });
  result.output = `${result.stdout ?? ""}${result.stderr ?? ""}`;
  return result;
}
