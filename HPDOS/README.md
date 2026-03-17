# HPDOS CLI

HPDOS is a command-line tool for [describe what HPDOS does].

## Quick Start

### Install

```bash
curl -fsSL https://raw.githubusercontent.com/YOUR_ORG/hpdos/main/scripts/install.sh | bash
```

Replace `YOUR_ORG` with your GitHub organization.

### 2. Start using

```bash
hpdos
```

This launches the interactive TUI chat session.

### Common Commands

```bash
hpdos              # Start interactive chat (default)
hpdos gui          # Open in system browser
hpdos providers    # Connect or manage AI providers
hpdos serve        # Run as a public server
hpdos help         # Show all commands
hpdos version      # Show version
```

For detailed documentation, see [Installation Guide](./INSTALL.md).

## Installation Methods

HPDOS supports multiple installation methods for different environments.

### Option 1: Automatic Installation (Recommended)

Downloads and installs the pre-built binary for your platform automatically:

```bash
curl -fsSL https://raw.githubusercontent.com/YOUR_ORG/hpdos/main/scripts/install.sh | bash
```

This script:
- Detects your OS and CPU architecture
- Downloads the correct binary from GitHub Releases
- Installs to `/usr/local/bin` (requires sudo for system-wide install)
- Adds `hpdos` to your PATH
- Verifies installation worked

### Option 2: Direct Download from GitHub Releases

Download and self-register using the built-in setup command:

1. Go to [GitHub Releases](https://github.com/YOUR_ORG/hpdos/releases)
2. Download binary for your platform (e.g., `hpdos-darwin-x64.tar.gz`)
3. Extract and auto-register:
   ```bash
   tar -xzf hpdos-darwin-x64.tar.gz
   ./hpdos setup
   ```

The `setup` command automatically installs to PATH on your system.

### Build from Source

Requirements: .NET 10.0 SDK

```bash
git clone https://github.com/YOUR_ORG/hpdos.git
cd hpdos
dotnet publish src-dotnet/HPDOS.CLI/HPDOS.CLI.csproj -c Release
```

The binary will be in `bin/Release/net10.0/<platform>/hpdos`

## Supported Platforms

- macOS 10.15+ (x64, ARM64)
- Linux glibc 2.17+ (x64, ARM64)
- Windows 10+ (x64, ARM64)

## Release Cadence

- **Stable**: Released weekly on [day]
- **Preview**: Released weekly on [day] for testing
- **Nightly**: Released daily from main branch

## Development

### Prerequisites

- .NET 10.0 SDK
- Git

### Building

```bash
dotnet build src-dotnet/HPDOS.CLI/HPDOS.CLI.csproj
```

### Testing

```bash
dotnet test
```

### Publishing

```bash
./scripts/publish.sh v0.1.0
```

## Contributing

[Link to CONTRIBUTING.md]

## License

[License info]

## Support

Report issues at: https://github.com/YOUR_ORG/hpdos/issues

---

Update `your-org` placeholders with your actual GitHub organization.
