import { postsQ } from "../fixtures/generated/base/index.js";

postsQ.lt("createdAt", "2026-01-01T00:00:00Z");
postsQ.contains("title", "HPD");

// @ts-expect-error boolean fields are not string-operation fields
postsQ.contains("published", true);

// @ts-expect-error unknown custom fields are not comparable
postsQ.lt("embedding", 1);
