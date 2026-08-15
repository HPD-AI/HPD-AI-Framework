# HPD.Gateway standalone Native AOT evidence

Run from any directory:

```bash
./run.sh
```

The harness publishes and executes the actual `HPD.Gateway.Standalone` Native
AOT process with SQLite, JWT/HPD.Auth, separate TLS data and management
listeners, target provisioning, inactive-target status, managed activation,
autonomous acknowledgement, exact duplicate replay across process restart,
generated OpenAPI, forwarding, and graceful shutdown.

It requires `dotnet`, `openssl`, `python3`, `curl`, and `jq`. The default runtime
identifier is `osx-arm64`; set `HPD_GATEWAY_E2E_RID` for another supported
Native AOT runtime.
