# @hpd/base-client

Zero-dependency TypeScript client for the phase-one HPD.BASE ASP.NET Core HTTP projection.

This package targets the implemented `HPD.Base.AspNetCore` routes under a mapped BASE prefix such as `/base`. It exposes metadata discovery, public/admin descriptors, collection CRUD/query, result-union and throwing methods, DTO-first query helpers, descriptor indexes, and explicit schema-aware hydration helpers.

It intentionally does not include generated clients, Studio UI, files, realtime/live query, streaming/export HTTP, search/vector, batch, transactions, upsert, schema writes/migrations, GraphQL/OpenAPI, policy explain, provider-native query syntax, or auth/session lifecycle helpers.

```ts
import { createBaseClient, q } from "@hpd/base-client";

const base = createBaseClient("/base");
const page = await base.collection("items").list({
  where: q.eq("title", "alpha"),
  sort: q.sortDesc("createdAt")
});
```

Node 18+ and modern browsers can use the global `fetch`. Older runtimes must pass `fetch` in the client config.

## Runtime Requirements

- ESM only.
- Node `>=18.0.0` or a modern browser with standard `fetch`.
- No fetch polyfill is bundled.
- Runtime dependencies are intentionally empty.

## Routes

Routes are relative to `baseUrl`.

- Metadata: `GET /manifest`, `/capabilities`, `/schema`, `/collections`, `/collections/{collectionId}`, `/health`, `/diagnostics`.
- Admin metadata: the same metadata routes under `/admin`.
- Records: `GET /collections/{collectionId}/records`, `POST /collections/{collectionId}/query`, `GET /collections/{collectionId}/records/{id}`, `POST /collections/{collectionId}/records`, `PATCH /collections/{collectionId}/records/{id}`, `PUT /collections/{collectionId}/records/{id}`, `DELETE /collections/{collectionId}/records/{id}`.

Record ids and collection ids are encoded with `encodeURIComponent`. There is no admin record CRUD surface.

## Results And Errors

Throwing methods such as `collection("items").get("1")` return the unwrapped DTO on success and throw `HpdBaseError` for HTTP, ProblemDetails, or transport failures.

Every throwing method has a `*Result` pair, such as `getResult`, that returns:

- `{ ok: true, status, value, httpStatus, headers }` for unwrapped ASP.NET success DTOs.
- `{ ok: false, status, error, httpStatus, headers, problem }` for RFC 7807 ProblemDetails and transport failures.

The SDK preserves HPD ProblemDetails extensions including `hpd.status`, `hpd.error.*`, `hpd.validation`, `hpd.conflict`, `hpd.capability`, `hpd.policy`, `hpd.store`, `hpd.warnings`, and `hpd.diagnostics`.

## Query Helpers

`q` and `createQueryBuilder()` emit BASE DTOs: `RecordQuery`, `FilterExpression`, and `QueryValue`. They do not emit SQL, provider-native syntax, JavaScript predicates, or executable expressions.

`collection.query()` always POSTs a `RecordQuery` DTO. `collection.list()` uses GET only for the implemented ASP.NET query-string grammar and falls back to POST when typed values or complex filters would lose fidelity.

## Metadata And Hydration

`bootstrap()` requests `schema,capabilities,health,collections` by default. Diagnostics are opt-in with `bootstrap({ diagnostics: true })`.

Public and admin metadata caches are separate. `bootstrapManifest` may preload metadata for the matching view. `contractVersion`, when supplied, is checked during bootstrap.

Raw record payloads preserve wire values. Date conversion is explicit through schema-aware hydration helpers such as `hydrateRecord`, `parseBaseDate`, `recordCreatedAtDate`, and `recordUpdatedAtDate`.
