# HPD Language Server Protocol code generator

Converts the pinned Microsoft LSP meta-model into checked-in protocol inventory used by the Coding harness.

```sh
dotnet run --project eng/HPD-Agent.LanguageProtocol.CodeGen -- \
  --schema eng/HPD-Agent.LanguageProtocol.CodeGen/Schema/metaModel.json \
  --output src/HPD-Agent.Harness/HPD-Agent.Harness.Coding/LanguageServer/Protocol/Generated
```

Add `--verify` to fail when checked-in output is stale. Generation never accesses the network.
