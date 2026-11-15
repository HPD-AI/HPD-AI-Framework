#!/usr/bin/env bash
set -euo pipefail

repository=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
cd "$repository"
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

framework=dotnet/HPD-Agent.Framework
contract="$scratch/openapi.json"
fixtures="$framework/sdk/conformance/fixtures/conformance-v1.json"

HPD_FULL_FEATURE_OPENAPI_OUTPUT="$contract" \
  dotnet test "$framework/test/HPD-Agent.AspNetCore.OpenApi.Tests" \
    --framework net11.0 \
    --filter FullyQualifiedName~WriteFullFeatureContractWhenRequested

dotnet run --project "$framework/src/HPD-Agent.SDK.Generator" -- \
  --target csharp \
  --input "$contract" \
  --output "$scratch/csharp" \
  --namespace HPD.Agent.Generated \
  --allow-public-api-change

dotnet run --project "$framework/src/HPD-Agent.SDK.Generator" -- \
  --target python \
  --input "$contract" \
  --output "$scratch/python"
python3 -m compileall -q "$scratch/python/hpd_agent_client" "$scratch/python/conformance_runner.py"
PYTHONPATH="$scratch/python" python3 "$scratch/python/conformance_runner.py" "$fixtures"

dotnet run --project "$framework/src/HPD-Agent.SDK.Generator" -- \
  --target go \
  --input "$contract" \
  --output "$scratch/go"
(
  cd "$scratch/go"
  go test ./...
  go run ./conformance "$repository/$fixtures"
)

dotnet run --project "$framework/src/HPD-Agent.SDK.Generator" -- \
  --target typescript \
  --input "$contract" \
  --output "$scratch/typescript"
(
  cd "$scratch/typescript"
  npm install --ignore-scripts --no-audit --no-fund
  npm test
  npm run type-test
  npm run conformance -- "$repository/$fixtures"
)
