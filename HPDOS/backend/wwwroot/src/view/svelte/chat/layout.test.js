import { describe, expect, test } from "bun:test";
import {
  appPaneWidthBounds,
  chatLayoutMode,
  clampAppPaneWidth,
  defaultAppPaneWidth
} from "./layout";

describe("chat layout policy", () => {
  test("maps sidebar collapsed state to chat layout mode", () => {
    expect(chatLayoutMode(false)).toBe("expanded");
    expect(chatLayoutMode(true)).toBe("collapsed");
  });

  test("uses expanded and collapsed default app shares", () => {
    expect(defaultAppPaneWidth("expanded", 1000)).toBe(450);
    expect(defaultAppPaneWidth("collapsed", 1000)).toBe(650);
  });

  test("keeps expanded app pane inside workspace/app bounds", () => {
    expect(clampAppPaneWidth("expanded", 100, 1000)).toBe(288);
    expect(clampAppPaneWidth("expanded", 900, 1000)).toBe(584);
    expect(clampAppPaneWidth("expanded", 420, 1000)).toBe(420);
  });

  test("keeps collapsed app pane inside workspace/app bounds", () => {
    expect(clampAppPaneWidth("collapsed", 100, 1000)).toBe(384);
    expect(clampAppPaneWidth("collapsed", 900, 1000)).toBe(680);
    expect(clampAppPaneWidth("collapsed", 520, 1000)).toBe(520);
  });

  test("degrades proportionally when the chat route is narrower than both minimums", () => {
    const expandedBounds = appPaneWidthBounds("expanded", 500);
    const collapsedBounds = appPaneWidthBounds("collapsed", 500);

    expect(expandedBounds.min).toBeCloseTo(204.545, 3);
    expect(expandedBounds.max).toBeCloseTo(204.545, 3);
    expect(collapsedBounds.min).toBeCloseTo(272.727, 3);
    expect(collapsedBounds.max).toBeCloseTo(272.727, 3);
  });
});
