# @hpd/base-client-files

Fetch-standard files/object-storage module client for HPD.BASE.

```ts
import { createBaseClient } from "@hpd/base-client";
import { createBaseFilesClient } from "@hpd/base-client-files";

const base = createBaseClient({
  baseUrl: "/base",
  headers: async () => ({ Authorization: `Bearer ${token}` })
});

const files = createBaseFilesClient(base);
const uploaded = await files.bucket("avatars").upload(file, {
  key: "users/u1/avatar.png",
  name: file.name
});

const metadata = await files.bucket("avatars").metadata(uploaded.metadata.objectId);
const response = await files.bucket("avatars").download(uploaded.metadata.objectId);
const blob = await files.bucket("avatars").downloadBlob(uploaded.metadata.objectId);
await files.bucket("avatars").delete(uploaded.metadata.objectId);
```

The package reuses the base client's configured fetch, credentials, headers, correlation id behavior, ProblemDetails parsing, and `BaseResult<T>` shape. Throwing methods unwrap results with the generic BASE error semantics; every operation also has a `*Result` variant.

Uploads are raw request bodies, not multipart. Browser `File` and `Blob` values work directly, and Node 18+ fetch-standard `Blob`, `ArrayBuffer`, typed arrays, strings, and streams can be used where fetch supports them.

Deferred from this first scaffold: auth lifecycle, provider SDKs, bucket CRUD, generated app-schema helpers, resumable uploads, signed URLs, thumbnails/transforms, scanning, CDN behavior, search/vector, GraphQL, batch, transactions, and schema writes.
