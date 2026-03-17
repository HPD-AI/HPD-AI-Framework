# HPDOS CLI Installation Guide

System requirements, installation methods, and release information.

## System Requirements

### Operating System
- macOS 10.15+
- Linux with glibc 2.17+ (Ubuntu 18.04+, CentOS 8+, Debian 9+)
- Windows 10+

### Hardware
- RAM: 2GB+ (minimal), 4GB+ (recommended)
- Storage: 100-200MB for binary

### Shell
- Bash, Zsh (macOS/Linux)
- PowerShell, Command Prompt (Windows)

## Installation Methods

### Recommended: Automatic Installation

Automatic installer detects your platform and downloads the correct binary:

```bash
curl -fsSL https://raw.githubusercontent.com/YOUR_ORG/hpdos/main/scripts/install.sh | bash
```

What it does:
1. Detects OS and CPU architecture
2. Downloads pre-built binary from GitHub Releases
3. Installs to `/usr/local/bin` (macOS/Linux) or Program Files (Windows)
4. Adds `hpdos` command to your PATH
5. Verifies installation

### Option 3: Direct Download from GitHub Releases (Auto-Register)

Download and use the binary's built-in setup:

1. Visit [GitHub Releases](https://github.com/YOUR_ORG/hpdos/releases)
2. Find the latest release
3. Download the binary for your platform:
   - macOS x64: `hpdos-darwin-x64.tar.gz`
   - macOS ARM64: `hpdos-darwin-arm64.tar.gz`
   - Linux x64: `hpdos-linux-x64.tar.gz`
   - Linux ARM64: `hpdos-linux-arm64.tar.gz`
   - Windows x64: `hpdos-windows-x64.zip`
   - Windows ARM64: `hpdos-windows-arm64.zip`

4. Extract and run setup:
   ```bash
   # macOS/Linux
   tar -xzf hpdos-darwin-x64.tar.gz
   ./hpdos setup

   # Windows
   # Extract the .zip file, then open PowerShell in that folder and run:
   .\hpdos setup
   ```

5. Verify:
   ```bash
   hpdos --version
   ```

The `hpdos setup` command will:
- Detect your OS
- Install to the correct system location
- Register in your PATH
- Prompt for sudo if needed (macOS/Linux)
- Handle Windows registry updates (Windows)

### Build from Source

For development or if pre-built binaries don't work:

```bash
git clone https://github.com/YOUR_ORG/hpdos.git
cd hpdos
dotnet publish src-dotnet/HPDOS.CLI/HPDOS.CLI.csproj -c Release
```

## Verify Installation

After installation, verify it works:

```bash
hpdos --version
hpdos --help
```

## Uninstall

```bash
sudo rm /usr/local/bin/hpdos
```

Or if you built from source, simply delete the binary and cloned directory.

## Release Channels

### Stable (Recommended)

Production-ready releases, tested and validated. Use for daily work.

```bash
# Downloaded automatically from Releases (latest tag)
curl -fsSL https://raw.githubusercontent.com/YOUR_ORG/hpdos/main/scripts/install.sh | bash
```

Released: [Weekly on Tuesdays]

### Preview

Not fully tested. May have issues. For early adopters to test new features.

Download from [GitHub Releases](https://github.com/YOUR_ORG/hpdos/releases) with `preview` tag.

Released: [Weekly on Fridays]

### Nightly

Latest development builds. Expected to have bugs. For contributors and testers.

Download from [GitHub Releases](https://github.com/YOUR_ORG/hpdos/releases) with `nightly` tag.

Released: Daily at UTC 00:00

## Troubleshooting

### Command not found after installation

The installer adds `hpdos` to your PATH. If it's not found:

1. Open a new terminal window
2. Verify `/usr/local/bin` is in your PATH:
   ```bash
   echo $PATH | grep /usr/local/bin
   ```
3. If not found, add it manually:
   ```bash
   # Add to ~/.bashrc or ~/.zshrc
   export PATH="/usr/local/bin:$PATH"
   ```
4. Reload your shell: `source ~/.zshrc` (or `.bashrc`)

### Permission denied during installation

The installer needs sudo permissions to write to `/usr/local/bin`. You'll be prompted for your password. This is normal.

If you want to install to a different location without sudo:

```bash
export INSTALL_DIR="$HOME/.local/bin"
curl -fsSL https://raw.githubusercontent.com/YOUR_ORG/hpdos/main/scripts/install.sh | bash
```

Then add `$HOME/.local/bin` to your PATH.

### Download fails with certificate error

Your system's certificate store may be outdated. Try:

```bash
# macOS
/usr/local/opt/openssl/bin/openssl version

# Linux
openssl version

# Update certificates if needed
# macOS: Install security updates via System Preferences
# Linux: sudo apt update && sudo apt upgrade ca-certificates
```

### Still having issues?

Please open an issue with:
- Your OS and version: `uname -a`
- Architecture: `uname -m`
- Error message
- Steps you took

https://github.com/YOUR_ORG/hpdos/issues

---

Replace `your-org` with your actual GitHub organization.
