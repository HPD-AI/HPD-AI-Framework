# HPDOS CLI Deployment Guide

## Overview

HPDOS CLI is distributed as **standalone, self-contained binaries** for all major platforms. No runtime dependencies required.

## Supported Platforms

| Platform | Binary | Size | Status |
|----------|--------|------|--------|
| macOS (Apple Silicon) | `hpdos-darwin-arm64` | ~80-100MB | ✓ Tested |
| macOS (Intel) | `hpdos-darwin-x64` | ~80-100MB | ✓ Tested |
| Linux (x86-64) | `hpdos-linux-x64` | ~80-100MB | ✓ Tested |
| Linux (ARM64) | `hpdos-linux-arm64` | ~80-100MB | ✓ Tested |
| Windows (x86-64) | `hpdos-windows-x64.exe` | ~85-105MB | ✓ Tested |
| Windows (ARM64) | `hpdos-windows-arm64.exe` | ~85-105MB | ✓ Tested |

## Installation

### macOS

```bash
# Download latest release
curl -L https://github.com/your-org/HPDOS/releases/latest/download/hpdos-darwin-arm64 -o hpdos

# Make executable
chmod +x hpdos

# Install to PATH (optional)
sudo mv hpdos /usr/local/bin/

# Verify
hpdos --version
```

### Linux

```bash
# Download latest release (choose x64 or arm64)
wget https://github.com/your-org/HPDOS/releases/latest/download/hpdos-linux-x64

# Make executable
chmod +x hpdos-linux-x64

# Install to PATH (optional)
sudo mv hpdos-linux-x64 /usr/local/bin/hpdos

# Verify
hpdos --version
```

### Windows

```powershell
# Download latest release from:
# https://github.com/your-org/HPDOS/releases/latest

# OR via PowerShell:
$url = "https://github.com/your-org/HPDOS/releases/latest/download/hpdos-windows-x64.exe"
Invoke-WebRequest -Uri $url -OutFile C:\hpdos.exe

# Add to PATH (optional, use System Properties → Environment Variables)
# Then run:
hpdos --version
```

## Configuration

### 1. Set Environment Variables

Copy `.env.example` to `.env` or set in your shell:

```bash
# Required: Choose your LLM provider
export HPDOS_PROVIDER_KEY=anthropic
export HPDOS_MODEL_ID=claude-sonnet-4-6

# Required: Add API key for your provider
export ANTHROPIC_API_KEY=sk-ant-...
```

### 2. Verify Configuration

```bash
# Test provider connectivity
hpdos providers

# This will:
# ✓ Detect configured providers
# ✓ Validate API keys exist
# ✓ Test provider connectivity
# ✓ List available models
```

## Running HPDOS

### TUI Chat Mode (Default)

```bash
hpdos
```

Starts an interactive TUI REPL. Type `/?` for help.

### GUI Browser Mode

```bash
hpdos gui
```

Opens HPDOS in your default browser at `http://localhost:5174`

### Backend-Only Mode

```bash
hpdos backend
```

Runs Kestrel server without opening browser. Useful for remote connections or integrations.

**Available endpoints:**
- Sessions API: `GET/POST/PATCH/DELETE http://localhost:5173/sessions`
- Branches: `GET/POST http://localhost:5173/sessions/{id}/branches`
- Streaming: `POST http://localhost:5173/sessions/{id}/branches/main/stream` (SSE)

### Custom Port

```bash
export HPDOS_PORT=8080
hpdos backend
```

Backend runs on `http://localhost:8080`

## Advanced Configuration

### Remote Server Mode

Connect to a remote HPDOS backend:

```bash
export HPDOS_REMOTE_URL=https://hpdos.example.com
hpdos chat
```

This connects the TUI directly to the remote backend without starting a local server.

### Max Iterations

Limit agentic iterations per session:

```bash
export HPDOS_MAX_TURNS=20
hpdos
```

### Logging

```bash
export LOG_LEVEL=Debug
export VERBOSE=1
hpdos chat
```

## Troubleshooting

### Binary Not Found

**Error:** `hpdos: command not found`

**Solution:**
```bash
# Add to PATH
export PATH="$PATH:/path/to/hpdos/binary"

# Or move to standard location
sudo cp ./hpdos /usr/local/bin/

# Verify
which hpdos
```

### Port Already in Use

**Error:** `Failed to bind port 5173`

**Solution:**
```bash
# Use different port
export HPDOS_PORT=5174
hpdos backend

# Or find what's using the port (macOS/Linux):
lsof -i :5173
```

### Provider Not Configured

**Error:** `No provider configured`

**Solution:**
```bash
# Set provider and API key
export HPDOS_PROVIDER_KEY=anthropic
export ANTHROPIC_API_KEY=sk-ant-...

# Verify
hpdos providers
```

### API Key Rejected

**Error:** `Invalid API key for provider`

**Solution:**
1. Verify API key is correct (check for trailing spaces)
2. Verify key has appropriate permissions
3. Check provider's API key format requirements
4. Test key with provider's CLI tool

### Binary Won't Execute (macOS)

**Error:** `cannot be opened because the developer cannot be verified`

**Solution:**
```bash
# Allow execution (one-time)
xattr -d com.apple.quarantine ./hpdos
chmod +x ./hpdos

# Then run
./hpdos --version
```

### Binary Won't Execute (Linux)

**Error:** `Permission denied`

**Solution:**
```bash
chmod +x ./hpdos
./hpdos --version
```

## Distribution

### Checksums

Verify binary integrity before using:

```bash
# macOS/Linux
shasum -a 256 hpdos-darwin-arm64

# Verify against CHECKSUMS.txt from release
shasum -a 256 -c CHECKSUMS.txt
```

### Installing from Release

1. Go to [GitHub Releases](https://github.com/your-org/HPDOS/releases)
2. Download binary for your platform
3. Verify checksum
4. Make executable: `chmod +x hpdos-*`
5. Move to PATH: `sudo mv hpdos-* /usr/local/bin/hpdos`

## Building from Source

If you need to customize or build your own binary:

```bash
# Publish all platforms
./scripts/publish.sh v0.1.0

# Publish single platform
./scripts/publish.sh v0.1.0 linux-x64

# Dry run (see what would happen)
./scripts/publish.sh v0.1.0 --dry-run
```

Binaries appear in `releases/v0.1.0/`

## CI/CD Integration

### GitHub Actions Workflow

Automated release builds trigger on git tags:

```bash
git tag v0.1.0
git push --tags
```

This automatically:
1. Builds all platform binaries
2. Generates checksums
3. Creates GitHub Release
4. Uploads binaries

## Support

- **Issues:** Report at https://github.com/your-org/HPDOS/issues
- **Documentation:** Check [README.md](README.md)
- **Config Help:** Run `hpdos help`
- **Provider Setup:** See `.env.example` for all provider options

## See Also

- [README.md](README.md) - General overview
- [CONFIGURATION_SUMMARY.md](CONFIGURATION_SUMMARY.md) - Known issues & fixes
- `.env.example` - All configuration options
