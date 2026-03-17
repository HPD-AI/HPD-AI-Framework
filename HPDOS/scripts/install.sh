#!/usr/bin/env bash
# HPDOS CLI installer - Downloads and installs to PATH
# Usage: curl https://your-domain/install.sh | bash

set -euo pipefail

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Helper functions for colored output
error() { echo -e "${RED}[ERROR]${NC} $*" >&2; }
success() { echo -e "${GREEN}[OK]${NC} $*"; }
info() { echo -e "${YELLOW}[INFO]${NC} $*"; }

# Configuration
GITHUB_REPO="YOUR_ORG/hpdos"  # UPDATE THIS
INSTALL_DIR="${INSTALL_DIR:-/usr/local/bin}"
BINARY_NAME="hpdos"

# Detect OS and architecture
detect_platform() {
    OS=$(uname -s)
    ARCH=$(uname -m)

    case "$OS" in
        Darwin)
            OS_NAME="darwin"
            case "$ARCH" in
                x86_64) ARCH_NAME="x64" ;;
                arm64) ARCH_NAME="arm64" ;;
                *) error "Unsupported architecture: $ARCH"; exit 1 ;;
            esac
            ARCHIVE_TYPE="tar.gz"
            ;;
        Linux)
            OS_NAME="linux"
            case "$ARCH" in
                x86_64) ARCH_NAME="x64" ;;
                aarch64) ARCH_NAME="arm64" ;;
                *) error "Unsupported architecture: $ARCH"; exit 1 ;;
            esac
            ARCHIVE_TYPE="tar.gz"
            ;;
        MINGW*|MSYS*|CYGWIN*)
            error "Windows not supported via this script"
            echo "Please download from: https://github.com/$GITHUB_REPO/releases"
            exit 1
            ;;
        *)
            error "Unsupported OS: $OS"
            exit 1
            ;;
    esac

    PLATFORM="${OS_NAME}-${ARCH_NAME}"
    success "Platform: $PLATFORM"
}

# Get latest release version
get_latest_version() {
    # Try to get from GitHub API
    if command -v curl &> /dev/null; then
        VERSION=$(curl -s "https://api.github.com/repos/$GITHUB_REPO/releases/latest" | grep '"tag_name"' | cut -d'"' -f4)
        if [[ -z "$VERSION" ]]; then
            VERSION="latest"
        fi
    else
        VERSION="latest"
    fi
    success "Version: $VERSION"
}

# Download and install
install_binary() {
    echo ""
    info "Downloading $BINARY_NAME ($PLATFORM)..."

    DOWNLOAD_URL="https://github.com/$GITHUB_REPO/releases/download/$VERSION/hpdos-${PLATFORM}.${ARCHIVE_TYPE}"
    TEMP_DIR=$(mktemp -d)
    TEMP_FILE="$TEMP_DIR/hpdos-${PLATFORM}.${ARCHIVE_TYPE}"

    # Download
    if ! curl -L "$DOWNLOAD_URL" -o "$TEMP_FILE" 2>/dev/null; then
        error "Failed to download from: $DOWNLOAD_URL"
        rm -rf "$TEMP_DIR"
        exit 1
    fi

    # Extract
    info "Extracting..."
    if [[ "$ARCHIVE_TYPE" == "tar.gz" ]]; then
        tar -xzf "$TEMP_FILE" -C "$TEMP_DIR"
    fi

    # Install (may need sudo)
    info "Installing to $INSTALL_DIR..."
    if [[ ! -w "$INSTALL_DIR" ]]; then
        echo "Need sudo permission to install to $INSTALL_DIR"
        sudo cp "$TEMP_DIR/$BINARY_NAME" "$INSTALL_DIR/$BINARY_NAME"
        sudo chmod +x "$INSTALL_DIR/$BINARY_NAME"
    else
        cp "$TEMP_DIR/$BINARY_NAME" "$INSTALL_DIR/$BINARY_NAME"
        chmod +x "$INSTALL_DIR/$BINARY_NAME"
    fi

    # Cleanup
    rm -rf "$TEMP_DIR"
}

# Verify installation
verify_install() {
    echo ""
    if command -v "$BINARY_NAME" &> /dev/null; then
        INSTALLED_VERSION=$($BINARY_NAME --version 2>/dev/null || echo "unknown")
        success "Installation successful!"
        echo "   Command: $BINARY_NAME"
        echo "   Version: $INSTALLED_VERSION"
        echo ""
        echo "Run '$BINARY_NAME --help' to get started."
        return 0
    else
        error "Installation verification failed"
        echo "Tried to install to: $INSTALL_DIR/$BINARY_NAME"
        echo "Make sure $INSTALL_DIR is in your PATH"
        return 1
    fi
}

# Main
main() {
    echo "HPDOS CLI Installer"
    echo ""

    detect_platform
    get_latest_version
    install_binary
    verify_install
}

main "$@"
