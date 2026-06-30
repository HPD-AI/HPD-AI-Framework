import type { HpdBaseClient } from "@hpd/base-client";
import { createGeneratedBaseClient, postsQ } from "../fixtures/generated/base/index.js";

declare const base: HpdBaseClient;

const db = createGeneratedBaseClient(base);

db.posts.create({ title: "Hello", authorId: "user_1" });
db.posts.patch("post_1", { body: null });
db.posts.replace("post_1", { title: "Hello", authorId: "user_1" });
db.posts.list({ where: postsQ.eq("authorId", "user_1"), select: ["title", "authorId"] });
db.posts.list({ where: q => q.eq("title", "Hello") });
db.collection("user-profiles").create({ displayName: "Ada" });
db.collections["user-profiles"].get("profile_1");
db.collection("unknown").get("id");
db.posts.$generic.query({ select: ["anyField"] });

// @ts-expect-error required create field is missing
db.posts.create({ title: "Hello" });

// @ts-expect-error replace requires create-shaped input
db.posts.replace("post_1", { body: "Only body" });

// @ts-expect-error select uses generated field paths
db.posts.list({ select: ["missing"] });

// @ts-expect-error field-path helper rejects unknown fields
postsQ.eq("missing", "value");
