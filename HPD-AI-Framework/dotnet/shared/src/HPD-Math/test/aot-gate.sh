#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root_dir="$(cd "$script_dir/.." && pwd)"

tests_project="$script_dir/HPD.Math.Tests/HPD.Math.Tests.csproj"
smoke_project="$script_dir/HPD.Math.AotSmoke/HPD.Math.AotSmoke.csproj"

case "$(uname -s)" in
  Darwin)
    case "$(uname -m)" in
      arm64) rid="osx-arm64" ;;
      x86_64) rid="osx-x64" ;;
      *) echo "Unsupported macOS architecture: $(uname -m)" >&2; exit 2 ;;
    esac
    binary_name="HPD.Math.AotSmoke"
    ;;
  Linux)
    case "$(uname -m)" in
      aarch64|arm64) rid="linux-arm64" ;;
      x86_64) rid="linux-x64" ;;
      *) echo "Unsupported Linux architecture: $(uname -m)" >&2; exit 2 ;;
    esac
    binary_name="HPD.Math.AotSmoke"
    ;;
  *)
    echo "Unsupported OS: $(uname -s)" >&2
    exit 2
    ;;
esac

publish_dir="$root_dir/artifacts/aot-smoke/$rid"
rm -rf "$publish_dir"

hot_path_projects=(
  "$root_dir/src/HPD.Math.Core"
  "$root_dir/src/HPD.Math.Finite"
  "$root_dir/src/HPD.Math.Algebra"
  "$root_dir/src/HPD.Math.Autodiff"
  "$root_dir/src/HPD.Math.Numerics"
  "$root_dir/src/HPD.Math.LinearAlgebra"
)

if find "${hot_path_projects[@]}" \
  -type f \( -name '*.cs' -o -name '*.csproj' \) \
  -not -path '*/bin/*' \
  -not -path '*/obj/*' \
  -print0 | xargs -0 grep -nE 'HPD\.Math\.(Managed|Text)'; then
  echo "Hot-path projects must not reference HPD.Math.Managed or HPD.Math.Text." >&2
  exit 1
fi

dotnet test "$tests_project" -warnaserror
dotnet publish "$smoke_project" \
  -c Release \
  -r "$rid" \
  --self-contained true \
  -warnaserror \
  /p:PublishDir="$publish_dir/"

"$publish_dir/$binary_name"
