import { q, type FieldPath } from "../src/index.js";

interface Item {
  title: string;
  rank: number;
}

const titlePath: FieldPath<Item> = "title";
q.eq<Item>(titlePath, "alpha");
q.sortDesc<Item>("rank");
q.query<Item>({ select: ["title"], where: helper => helper.gt<Item>("rank", 1) });
