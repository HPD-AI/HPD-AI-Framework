import { readFile, rm, writeFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";
import { main } from "../src/cli.js";

describe("CLI", () => {
  it("generates from a fixture snapshot and cleans stale files", async () => {
    await rm("fixtures/generated/cli-base", { recursive: true, force: true });
    await main(["generate", "--snapshot", "fixtures/base-client-snapshot.json", "--out", "fixtures/generated/cli-base", "--clean"]);
    const client = await readFile("fixtures/generated/cli-base/client.ts", "utf8");
    expect(client).toContain("createGeneratedBaseClient");
    await writeFile("fixtures/generated/cli-base/stale.txt", "stale", "utf8");
    await main(["generate", "--snapshot", "fixtures/base-client-snapshot.json", "--out", "fixtures/generated/cli-base", "--clean"]);
    await expect(readFile("fixtures/generated/cli-base/stale.txt", "utf8")).rejects.toThrow();
  });

  it("exits through errors for invalid input", async () => {
    await expect(main(["generate", "--snapshot", "fixtures/missing.json", "--out", "fixtures/generated/base"])).rejects.toThrow();
  });
});
