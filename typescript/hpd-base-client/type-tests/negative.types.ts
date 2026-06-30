import { createBaseClient } from "../src/index.js";

const client = createBaseClient({ baseUrl: "/base", fetch: async () => new Response("{}") });

// @ts-expect-error upsert is deferred and absent in phase one.
client.collection("items").upsert("1", {});

// @ts-expect-error files are deferred and absent in phase one.
client.files;

// @ts-expect-error auth lifecycle helpers are deferred and absent in phase one.
client.login("user", "password");
