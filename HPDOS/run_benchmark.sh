#!/bin/bash
# Quick runner script for HPDOS Harbor agent
# Usage: ./run_benchmark.sh [num_trials] [model]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TRIALS=${1:-1}
MODEL=${2:-"anthropic/claude-sonnet-4-6"}
IMAGE=${3:-"hpdos-terminal-bench:latest"}
DATASET=${4:-"terminal-bench@2.0"}

echo "🚀 Running HPDOS Harbor Benchmark"
echo "  Dataset:    $DATASET"
echo "  Model:      $MODEL"
echo "  Trials:     $TRIALS"
echo "  Image:      $IMAGE"
echo ""

cd "$SCRIPT_DIR"

# Check if Docker image exists
if ! docker images --format "{{.Repository}}:{{.Tag}}" | grep -q "^$IMAGE\$"; then
    echo "📦 Image not found, building..."
    docker build -f harbor-agent/Dockerfile -t "$IMAGE" .
fi

echo "▶️  Starting benchmark..."
echo ""

harbor run -d "$DATASET" \
  --agent-import-path hpdos_harbor.agent:HPDOSAgent \
  --env docker \
  -m "$MODEL" \
  --ek image="$IMAGE" \
  -l "$TRIALS"

echo ""
echo "✅ Benchmark complete! Check jobs/ directory for results."
