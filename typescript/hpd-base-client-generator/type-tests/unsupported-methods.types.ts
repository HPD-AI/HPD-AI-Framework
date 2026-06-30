import type { HpdBaseClient } from "@hpd/base-client";
import { createGeneratedBaseClient } from "../fixtures/generated/base/index.js";

declare const base: HpdBaseClient;
const db = createGeneratedBaseClient(base);

// @ts-expect-error unsupported by collection operation matrix
db.posts.delete("post_1");

// @ts-expect-error read-only collection omits create
db.auditLog.create({ message: "created", createdAt: "2026-01-01T00:00:00Z" });

// @ts-expect-error anti-scope: no generated upsert
db.posts.upsert({ title: "Hello", authorId: "user_1" });

// @ts-expect-error anti-scope: no files module
db.files;

// @ts-expect-error anti-scope: no batch module
db.batch;

// @ts-expect-error anti-scope: no realtime module
db.realtime;

// @ts-expect-error anti-scope: no live query module
db.liveQuery;

// @ts-expect-error anti-scope: no stream helper
db.stream;

// @ts-expect-error anti-scope: no search module
db.search;

// @ts-expect-error anti-scope: no vector module
db.vector;

// @ts-expect-error anti-scope: no transactions module
db.transactions;

// @ts-expect-error anti-scope: no schema-write module
db.schemaWrite;

// @ts-expect-error anti-scope: no policy explain helper
db.policyExplain;

// @ts-expect-error anti-scope: no GraphQL helper
db.graphql;

// @ts-expect-error anti-scope: no OpenAPI helper
db.openapi;

// @ts-expect-error anti-scope: no auth lifecycle
db.login();

// @ts-expect-error anti-scope: no auth lifecycle
db.logout();
