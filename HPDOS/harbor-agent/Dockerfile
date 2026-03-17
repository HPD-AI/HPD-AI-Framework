# HPDOS Docker image for Harbor Terminal-Bench
FROM ubuntu:22.04

# Install runtime dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 curl ca-certificates ca-certificates libssl3 libicu70 \
    && rm -rf /var/lib/apt/lists/*

# Install .NET 10 runtime (required by hpdos binary)
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- \
    --channel 10.0 \
    --runtime dotnet \
    --install-dir /usr/local/dotnet

# Copy pre-built hpdos and all dependencies
COPY harbor-agent/hpdos-publish /usr/local/hpdos

# Make binary executable
RUN chmod +x /usr/local/hpdos/hpdos

# Create symlink in standard bin location (not /usr/local/bin which might not be in PATH)
RUN ln -sf /usr/local/hpdos/hpdos /bin/hpdos

# Set environment permanently
ENV DOTNET_ROOT=/usr/local/dotnet
ENV PATH="/usr/local/dotnet:/usr/local/hpdos:$PATH"
ENV LD_LIBRARY_PATH="/usr/local/hpdos:$LD_LIBRARY_PATH"

# Logs directory (Harbor convention)
RUN mkdir -p /logs/agent /logs/artifacts

# Default working directory
WORKDIR /app

CMD ["hpdos", "--help"]
