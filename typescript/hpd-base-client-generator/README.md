# @hpd/base-client-generator

Generates schema-specific TypeScript wrappers over `@hpd/base-client`.

```bash
hpd-base-client-generator generate \
  --snapshot ./base-client-snapshot.json \
  --out ./src/generated/base
```

Generated runtime code imports only `@hpd/base-client` and delegates all HTTP behavior to an existing `HpdBaseClient`.
