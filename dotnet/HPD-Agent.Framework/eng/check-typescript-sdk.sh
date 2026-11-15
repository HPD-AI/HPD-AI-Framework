#!/usr/bin/env bash
set -euo pipefail

repository=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
cd "$repository"
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

framework=dotnet/HPD-Agent.Framework
client=typescript/hpd-agent-client

dotnet restore "$framework/src/HPD-Agent.EventPrecompiler"
HPD_FULL_FEATURE_OPENAPI_OUTPUT="$scratch/contract.json" \
  dotnet test "$framework/test/HPD-Agent.AspNetCore.OpenApi.Tests" \
    --framework net11.0 \
    --filter FullyQualifiedName~WriteFullFeatureContractWhenRequested

dotnet run --project "$framework/src/HPD-Agent.SDK.Generator" -- \
  --target typescript --input "$scratch/contract.json" --output "$scratch/client"

diff -ru "$client/src" "$scratch/client/src"
for file in sdk-ir.json hpd-client.generated.json README.md package.json conformance.mjs; do
  diff -u "$client/$file" "$scratch/client/$file"
done
