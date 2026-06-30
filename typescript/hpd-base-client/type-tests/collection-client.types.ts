import { createBaseClient, type CollectionClient } from "../src/index.js";

interface Item {
  title: string;
  rank?: number;
}

const client = createBaseClient({ baseUrl: "/base", fetch: async () => new Response("{}") });
const collection: CollectionClient<Item> = client.collection<Item>("items");

await collection.create({ title: "alpha" });
await collection.patch("1", { rank: 2 });
await collection.replace("1", { title: "beta" });

// @ts-expect-error title is required for a plain replace payload.
await collection.replace("1", { rank: 3 });
