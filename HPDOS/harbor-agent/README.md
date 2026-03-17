# HPDOS Harbor Agent

Harbor integration for the HPDOS CLI agent with Terminal-Bench benchmarking.

## Quick Start

### Prerequisites
- Docker installed and running
- Harbor CLI installed
- HPDOS binary built (included in `hpdos-publish/`)

### Build Docker Image

```bash
cd /Users/einsteinessibu/Documents/HPDOS
docker build -f harbor-agent/Dockerfile -t hpdos-terminal-bench:latest .
```

### Run Benchmark

```bash
cd /Users/einsteinessibu/Documents/HPDOS && harbor run -d terminal-bench@2.0 \
  --agent-import-path hpdos_harbor.agent:HPDOSAgent \
  --env docker \
  -m anthropic/claude-sonnet-4-6 \
  --ek image=hpdos-terminal-bench:latest \
  -l 1
```

### Configuration Options

| Option | Description | Default |
|--------|-------------|---------|
| `-m, --model` | LLM model name | `anthropic/claude-sonnet-4-6` |
| `-l` | Number of trials | `1` |
| `--ek image=` | Docker image to use | `hpdos-terminal-bench:latest` |
| `-d DATASET` | Benchmark dataset | `terminal-bench@2.0` |

### Environment Variables

Set these for the agent runtime:

```bash
export HPDOS_PORT=5173                    # Port for HPDOS backend
export HPDOS_MAX_TURNS=50                 # Max conversation turns
export HPDOS_PROVIDER_KEY=anthropic       # LLM provider
export ANTHROPIC_API_KEY=sk-...           # API key
```

## Output

Results are saved to `jobs/TIMESTAMP/` directories:

```
jobs/2026-03-15__09-56-00/
├── result.json                           # Overall results
├── gpt2-codegolf__33BpgNL/
│   ├── agent/
│   │   ├── trajectory.json              # ATIF trajectory output
│   │   ├── backend.log                  # HPDOS backend logs
│   │   ├── driver.log                   # Driver script logs
│   │   └── setup/                       # Setup logs
│   ├── result.json                      # Trial result
│   └── verifier/                        # Benchmark verification
```

## Known Limitations

- **ARM64 macOS**: The x86-64 hpdos binary cannot execute directly. Use Docker emulation or deploy to x86-64 Linux.
- **Fallback Mode**: If backend fails to start, agent generates a minimal valid trajectory instead of failing.

## Deployment

### On x86-64 Linux

```bash
docker build -f harbor-agent/Dockerfile -t hpdos-terminal-bench:latest .
harbor run -d terminal-bench@2.0 \
  --agent-import-path hpdos_harbor.agent:HPDOSAgent \
  --env docker \
  -m anthropic/claude-sonnet-4-6 \
  --ek image=hpdos-terminal-bench:latest \
  -l 10  # Run 10 trials
```

### On ARM64 macOS (with emulation warning)

Use `docker buildx` for cross-platform builds:

```bash
docker buildx build --platform linux/x86_64 \
  -f harbor-agent/Dockerfile \
  -t hpdos-terminal-bench:x86_64 \
  . --load
```

## Files

- `agent.py` - Harbor agent adapter
- `install-hpdos.sh.j2` - Installation script template
- `Dockerfile` - Docker image definition
- `hpdos-publish/` - Precompiled HPDOS binaries and dependencies

## Troubleshooting

### Backend not starting
- Check `backend.log` for errors
- Verify `hpdos` binary is accessible: `docker run --rm hpdos-terminal-bench:latest which hpdos`
- Ensure .NET runtime is installed: `docker run --rm hpdos-terminal-bench:latest dotnet --version`

### Connection refused
- Backend startup failed (see above)
- Agent generates fallback trajectory automatically
- Check logs in `agent/` directory

### Slow execution on macOS
- x86-64 emulation overhead is ~5-7x slower
- Deploy to native x86-64 Linux for production
