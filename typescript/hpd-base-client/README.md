# @hpd/base-client

Framework-neutral TypeScript client for HPD Base protocol v2. Generate an immutable schema with
`@hpd/base-client-generator`, then pass it directly to `createBaseClient`.

```ts
const base = createBaseClient({ url: "/base", schema });
await base.documents.get(id);
await base.documents.create(value);
await base.documents.query({ where, take: 50 }).execute();
```

Expected server outcomes use `BaseResult<T>`. Use `unwrap` only when exception flow is deliberate.
