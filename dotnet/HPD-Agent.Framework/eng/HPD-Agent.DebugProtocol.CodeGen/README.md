# HPD Debug Adapter Protocol code generator

This engineering tool converts the pinned Microsoft Debug Adapter Protocol JSON schema into the
checked-in, Native-AOT-safe wire model used by `HPD-Agent.Harness.Coding`.

Generate from the framework root:

```sh
dotnet run --project eng/HPD-Agent.DebugProtocol.CodeGen -- \
  --schema eng/HPD-Agent.DebugProtocol.CodeGen/Schema/debugAdapterProtocol.json \
  --output src/HPD-Agent.Harness/HPD-Agent.Harness.Coding/Debugging/Protocol/Generated
```

Verify that checked-in output is current:

```sh
dotnet run --project eng/HPD-Agent.DebugProtocol.CodeGen -- \
  --schema eng/HPD-Agent.DebugProtocol.CodeGen/Schema/debugAdapterProtocol.json \
  --output src/HPD-Agent.Harness/HPD-Agent.Harness.Coding/Debugging/Protocol/Generated \
  --verify
```

Schema upgrades require updating the pinned file, checksum and commit metadata together, reviewing
the generated diff, updating `Schema/UPSTREAM.md`, and running the Coding harness test suite.
