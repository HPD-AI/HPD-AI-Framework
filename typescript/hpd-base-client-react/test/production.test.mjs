import assert from "node:assert/strict";
import test from "node:test";

test("React production server rendering is subscription-free", async () => {
  process.env.NODE_ENV = "production";
  const [{ createElement }, { renderToString }, { useBaseQuery }] = await Promise.all([import("react"), import("react-dom/server"), import("../dist/index.js")]);
  let watched = 0; const query = { watch() { watched++; return { closed: false, close() {} }; } };
  function Component() { return createElement("span", null, useBaseQuery(query).kind); }
  assert.equal(renderToString(createElement(Component)), "<span>loading</span>"); assert.equal(watched, 0);
});
