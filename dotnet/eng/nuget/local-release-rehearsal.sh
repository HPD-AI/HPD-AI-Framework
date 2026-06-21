#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
REPO_ROOT="$(cd "$ROOT/.." && pwd)"
VERSION="${VERSION:-0.2.0-rehearsal}"
FEED="${FEED:-/private/tmp/hpd-local-nuget-feed}"
WORK="${WORK:-/private/tmp/hpd-local-release-rehearsal}"
RHODIUM_COOKBOOK="${RHODIUM_COOKBOOK:-/Users/ewoof/Desktop/Rhodium/cookbook}"
HPD_AGENT_COOKBOOK="${HPD_AGENT_COOKBOOK:-/Users/ewoof/Desktop/HPD-Agent/cookbook}"

VERSION_PREFIX="${VERSION%%-*}"
VERSION_SUFFIX=""
if [[ "$VERSION" == *-* ]]; then
  VERSION_SUFFIX="${VERSION#*-}"
fi

pack_project() {
  local project="$1"
  local args=(
    "$project"
    -c Release
    -m:1
    -o "$FEED"
    -p:IsPackable=true
    -p:NuGetAudit=false
    -p:VersionPrefix="$VERSION_PREFIX"
  )

  if [[ -n "$VERSION_SUFFIX" ]]; then
    args+=("-p:VersionSuffix=$VERSION_SUFFIX")
  fi

  echo "Packing ${project#$REPO_ROOT/}"
  MSBUILDDISABLENODEREUSE=1 dotnet pack "${args[@]}"
}

rewrite_cookbook() {
  local source="$1"
  local destination="$2"
  mkdir -p "$(dirname "$destination")"
  VERSION="$VERSION" perl -pe 's/^#:package ([A-Za-z0-9_.-]+)@[A-Za-z0-9_.-]+/"#:package $1\@$ENV{VERSION}"/e' "$source" > "$destination"
}

run_cookbook() {
  local source="$1"
  local mode="$2"
  local name="${source#$RHODIUM_COOKBOOK/}"
  name="${name#$HPD_AGENT_COOKBOOK/}"
  local dest="$WORK/cookbooks/$name"

  rewrite_cookbook "$source" "$dest"
  echo "Checking cookbook $name ($mode)"

  if [[ "$mode" == "run" ]]; then
    dotnet run "$dest"
  else
    dotnet build "$dest" -v:minimal
  fi
}

rm -rf "$FEED" "$WORK"
mkdir -p "$FEED" "$WORK/cookbooks" "$WORK/packages" "$WORK/home"
export NUGET_PACKAGES="$WORK/packages"
export DOTNET_CLI_HOME="$WORK/home"
export HOME="$WORK/home"

cat > "$WORK/NuGet.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-release" value="$FEED" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

pushd "$WORK" >/dev/null

RHODIUM_PROJECTS=(
  "$ROOT/shared/src/HPD-Events/HPD-Events.csproj"
  "$ROOT/shared/src/Rhodium/src/Rhodium.Core/Rhodium.Core.csproj"
  "$ROOT/shared/src/Rhodium/src/Rhodium.Data/Rhodium.Data.csproj"
  "$ROOT/shared/src/Rhodium/src/Rhodium.SourceGenerators/Rhodium.SourceGenerators.csproj"
  "$ROOT/shared/src/Rhodium/src/Rhodium.Analyzers/Rhodium.Analyzers.csproj"
  "$ROOT/shared/src/Rhodium/src/Rhodium.Simulation/Rhodium.Simulation.csproj"
  "$ROOT/shared/src/Rhodium/src/Rhodium.Connectivity/Rhodium.Connectivity.csproj"
)

HPD_AGENT_PROJECTS=(
  "$ROOT/shared/src/HPD-Serialization/HPD-Serialization.csproj"
  "$ROOT/shared/src/HPD-Events/HPD-Events.csproj"
  "$ROOT/shared/src/HPD-OpenApi.Core/HPD-OpenApi.Core.csproj"
  "$ROOT/shared/src/HPD-TextExtract/HPD-TextExtract.csproj"
  "$ROOT/shared/src/HPD-RealtimeMedia/src/HPD-Buffers/HPD-Buffers.csproj"
  "$ROOT/shared/src/HPD-RealtimeMedia/src/HPD-Audio.Primitives/HPD-Audio.Primitives.csproj"
  "$ROOT/HPD-TUI.Framework/src/HPD-TUI.csproj"
  "$ROOT/HPD-Graph.Framework/src/HPD-Graph.Abstractions/HPD-Graph.Abstractions.csproj"
  "$ROOT/HPD-Graph.Framework/src/HPD-Graph.SourceGenerator/HPD-Graph.SourceGenerator.csproj"
  "$ROOT/HPD-Graph.Framework/src/HPD-Graph.Core/HPD-Graph.Core.csproj"
  "$ROOT/HPD-Agent.Framework/src/HPD-Agent/HPD-Agent.csproj"
  "$ROOT/HPD-Agent.Framework/src/HPD-Agent.Audio/HPD-Agent.Audio.csproj"
  "$ROOT/HPD-Agent.Framework/src/HPD-Agent.Hosting/HPD-Agent.Hosting.csproj"
  "$ROOT/HPD-Agent.Framework/src/HPD-Agent.Evaluations/HPD-Agent.Evaluations.csproj"
  "$ROOT/HPD-Agent.Framework/src/HPD-Agent.AspNetCore/HPD-Agent.AspNetCore.csproj"
  "$ROOT/HPD-Agent.Framework/src/HPD-Agent.MultiAgent/HPD-Agent.MultiAgent.csproj"
  "$ROOT/HPD-Agent.Framework/src/HPD-Agent.TUI/HPD-Agent.TUI.csproj"
  "$ROOT/HPD-Agent.Framework/src/HPD-Agent.Harness/HPD-Agent.Harness.Coding/HPD-Agent.Harness.Coding.csproj"
  "$ROOT/HPD-Agent.Framework/src/HPD-Agent.Harness/HPD-Agent.Harness.Coding.TUI/HPD-Agent.Harness.Coding.TUI.csproj"
  "$ROOT/HPD-Agent.Framework/src/HPD-Agent.Providers/HPD-Agent.Providers.OpenAI/HPD-Agent.Providers.OpenAI.csproj"
)

case "${PACK_SCOPE:-all}" in
  rhodium)
    PROJECTS=("${RHODIUM_PROJECTS[@]}")
    ;;
  hpd-agent)
    PROJECTS=("${HPD_AGENT_PROJECTS[@]}")
    ;;
  all)
    PROJECTS=("${RHODIUM_PROJECTS[@]}" "${HPD_AGENT_PROJECTS[@]}")
    ;;
  *)
    echo "PACK_SCOPE must be one of: all, rhodium, hpd-agent" >&2
    exit 2
    ;;
esac

"$ROOT/eng/nuget/validate-package-identities.sh" "${PROJECTS[@]}"

for project in "${PROJECTS[@]}"; do
  pack_project "$project"
done

if [[ "${RUN_RHODIUM_COOKBOOKS:-true}" == "true" ]]; then
  run_cookbook "$RHODIUM_COOKBOOK/GettingStarted/01-first-backtest.cs" run
  run_cookbook "$RHODIUM_COOKBOOK/Data/aggregate-bars.cs" run
  run_cookbook "$RHODIUM_COOKBOOK/StrategyAuthoring/generated-fields.cs" run
  run_cookbook "$RHODIUM_COOKBOOK/Simulation/fast-vector-backtest.cs" run
fi

if [[ "${RUN_HPD_AGENT_COOKBOOKS:-true}" == "true" ]]; then
  run_cookbook "$HPD_AGENT_COOKBOOK/GettingStarted/03-tools.cs" build
  run_cookbook "$HPD_AGENT_COOKBOOK/GettingStarted/04-tool-harness.cs" build
  run_cookbook "$HPD_AGENT_COOKBOOK/GettingStarted/10-aspnet-hosting.cs" build
fi

popd >/dev/null

echo "Local release rehearsal passed."
echo "Feed: $FEED"
echo "Work: $WORK"
