import { describe, expect, it, vi } from "vitest";
import { createGatewayClient } from "../src/index.js";

const maximumBodyBytes = 8 * 1024 * 1024;
const success = '{"apiVersion":"1.0.0","capabilities":[]}';
const authentication = { getAccessToken: async () => ({ value: "token" }) };

const call = async (body: BodyInit, headers: HeadersInit = { "content-type": "application/json" }) => {
  const client = createGatewayClient({
    baseUrl: "https://gateway.example",
    authentication,
    fetch: async () => new Response(body, { status: 200, headers }),
  });
  return client.capabilities({ path: {} });
};

const expectParsedAtMaximum = async (text: string) => {
  expect(await call(text)).toMatchObject({ kind: "protocol", reason: "schema-mismatch" });
};

const expectBoundExceeded = async (text: string) => {
  expect(await call(text)).toMatchObject({ kind: "protocol", reason: "response-too-large" });
};

describe("Gateway response and parser bounds", () => {
  it("accepts exactly 128 response headers and rejects 129", async () => {
    const exact = new Headers({ "content-type": "application/json" });
    for (let index = 0; index < 127; index++) exact.set(`x-${index}`, "v");
    expect((await call(success, exact)).ok).toBe(true);
    exact.set("x-overflow", "v");
    expect(await call(success, exact)).toMatchObject({ kind: "protocol", reason: "response-too-large" });
  });

  it("accepts 128-byte header names and 4,096-byte values and rejects maximum+1", async () => {
    const exactName = new Headers({ "content-type": "application/json", [`x${"a".repeat(127)}`]: "v" });
    expect((await call(success, exactName)).ok).toBe(true);
    const longName = new Headers({ "content-type": "application/json", [`x${"a".repeat(128)}`]: "v" });
    expect(await call(success, longName)).toMatchObject({ kind: "protocol", reason: "response-too-large" });

    expect((await call(success, { "content-type": "application/json", "x-value": "x".repeat(4_096) })).ok).toBe(true);
    expect(await call(success, { "content-type": "application/json", "x-value": "x".repeat(4_097) })).toMatchObject({ kind: "protocol", reason: "response-too-large" });
  });

  it("accepts a 256-byte media type field and rejects 257 bytes", async () => {
    const media = "application/json;";
    expect((await call(success, { "content-type": media + "x".repeat(256 - media.length) })).ok).toBe(true);
    expect(await call(success, { "content-type": media + "x".repeat(257 - media.length) })).toMatchObject({ kind: "protocol", reason: "unexpected-media-type" });
  });

  it("accepts exactly 8 MiB and rejects 8 MiB plus one byte", async () => {
    const exact = success + " ".repeat(maximumBodyBytes - success.length);
    expect((await call(exact)).ok).toBe(true);
    expect(await call(exact + " ")).toMatchObject({ kind: "protocol", reason: "response-too-large" });
  });

  it("accepts JSON depth 64 and rejects depth 65", async () => {
    await expectParsedAtMaximum("[".repeat(64) + "0" + "]".repeat(64));
    await expectBoundExceeded("[".repeat(65) + "0" + "]".repeat(65));
  });

  it("accepts 256 object properties and rejects 257", async () => {
    const object = (count: number) => `{${Array.from({ length: count }, (_, index) => `"p${index}":0`).join(",")}}`;
    await expectParsedAtMaximum(object(256));
    await expectBoundExceeded(object(257));
  });

  it("accepts 10,000 array items and rejects 10,001", async () => {
    const array = (count: number) => `[${Array.from({ length: count }, () => "0").join(",")}]`;
    await expectParsedAtMaximum(array(10_000));
    await expectBoundExceeded(array(10_001));
  });

  it("accepts exactly 750,000 lexical tokens and rejects 750,001", async () => {
    const tokenDocument = (extra: number) => {
      const counts = [...Array(74).fill(9_998), 9_996 + extra] as number[];
      return `[${counts.map(count => `[${Array.from({ length: count }, () => "0").join(",")}]`).join(",")}]`;
    };
    await expectParsedAtMaximum(tokenDocument(0));
    await expectBoundExceeded(tokenDocument(1));
  });

  it("accepts a 16,384-byte token and rejects 16,385 before Fetch", async () => {
    const exactFetch = vi.fn(async () => new Response(success, { headers: { "content-type": "application/json" } }));
    const exact = createGatewayClient({ baseUrl: "https://gateway.example", authentication: { getAccessToken: () => ({ value: "x".repeat(16_384) }) }, fetch: exactFetch });
    expect((await exact.capabilities({ path: {} })).ok).toBe(true);
    expect(exactFetch).toHaveBeenCalledOnce();

    const overflowFetch = vi.fn();
    const overflow = createGatewayClient({ baseUrl: "https://gateway.example", authentication: { getAccessToken: () => ({ value: "x".repeat(16_385) }) }, fetch: overflowFetch });
    expect(await overflow.capabilities({ path: {} })).toMatchObject({ kind: "protocol", reason: "schema-mismatch" });
    expect(overflowFetch).not.toHaveBeenCalled();
  });

  it("uses standard header folding and abort semantics in Node", async () => {
    const headers = new Headers([["content-type", "application/json"], ["x-folded", "one"], ["x-folded", "two"]]);
    const client = createGatewayClient({ baseUrl: "https://gateway.example", authentication, fetch: async () => new Response(success, { headers }) });
    const result = await client.capabilities({ path: {} });
    expect(result).toMatchObject({ ok: true, headers: { "x-folded": "one, two" } });

    const controller = new AbortController(); controller.abort();
    expect(await client.capabilities({ path: {} }, { signal: controller.signal })).toMatchObject({ kind: "canceled", reason: "caller-canceled" });

    const duringRead = new AbortController();
    const interrupted = createGatewayClient({
      baseUrl: "https://gateway.example",
      authentication,
      fetch: async () => new Response(new ReadableStream<Uint8Array>({
        start(stream) { queueMicrotask(() => { duringRead.abort(); stream.error(new Error("aborted")); }); },
      }), { headers: { "content-type": "application/json" } }),
    });
    expect(await interrupted.capabilities({ path: {} }, { signal: duringRead.signal })).toMatchObject({ kind: "canceled", reason: "caller-canceled" });
  });
});
