import assert from "node:assert/strict";
import test from "node:test";
import React, { StrictMode } from "react";
import { renderToString } from "react-dom/server";
import TestRenderer, { act } from "react-test-renderer";
import { useBaseQuery } from "../dist/index.js";

test("server rendering returns loading without opening a subscription", () => {
  const query = fakeQuery();
  function View() { return React.createElement("span", null, useBaseQuery(query).kind); }
  assert.match(renderToString(React.createElement(View)), /loading/);
  assert.equal(query.watchCount, 0);
});

test("Strict Mode shares the delayed core subscription lifecycle", async () => {
  const query = fakeQuery();
  function View() { return React.createElement("span", null, useBaseQuery(query).kind); }
  let renderer;
  await act(async () => { renderer = TestRenderer.create(React.createElement(StrictMode, null, React.createElement(View))); });
  assert.equal(query.watchCount, 1);
  await act(async () => { renderer.unmount(); });
  assert.equal(query.closeCount, 1);
});

function fakeQuery() {
  return {
    watchCount: 0, closeCount: 0,
    watch() { this.watchCount++; let closed = false; const owner = this; return { get closed() { return closed; }, close() { if (!closed) { closed = true; owner.closeCount++; } } }; }
  };
}
