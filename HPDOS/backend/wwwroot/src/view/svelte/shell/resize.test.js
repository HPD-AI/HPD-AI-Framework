import { describe, expect, test } from "bun:test";
import { clampAppPaneWidth, defaultAppPaneWidth } from "./layout";
import { appPaneWidthForKeyboardResize } from "./controller";

describe("shell resize keyboard policy", () => {
  test("arrow keys resize the app pane from the divider perspective", () => {
    expect(appPaneWidthForKeyboardResize("ArrowLeft", "expanded", 450, 1000)).toBe(474);
    expect(appPaneWidthForKeyboardResize("ArrowRight", "expanded", 450, 1000)).toBe(426);
  });

  test("shift arrow keys use the larger resize step", () => {
    expect(appPaneWidthForKeyboardResize("ArrowLeft", "collapsed", 650, 1000, true)).toBe(746);
    expect(appPaneWidthForKeyboardResize("ArrowRight", "collapsed", 650, 1000, true)).toBe(554);
  });

  test("home and end target the legal app pane bounds", () => {
    expect(appPaneWidthForKeyboardResize("Home", "expanded", 450, 1000)).toBe(288);
    expect(appPaneWidthForKeyboardResize("End", "expanded", 450, 1000)).toBe(584);

    expect(appPaneWidthForKeyboardResize("Home", "collapsed", 650, 1000)).toBe(384);
    expect(appPaneWidthForKeyboardResize("End", "collapsed", 650, 1000)).toBe(680);
  });

  test("enter resets to the current mode default", () => {
    expect(appPaneWidthForKeyboardResize("Enter", "expanded", 300, 1000)).toBe(defaultAppPaneWidth("expanded", 1000));
    expect(appPaneWidthForKeyboardResize("Enter", "collapsed", 400, 1000)).toBe(defaultAppPaneWidth("collapsed", 1000));
  });

  test("unsupported keys are ignored", () => {
    expect(appPaneWidthForKeyboardResize("Tab", "expanded", 450, 1000)).toBeNull();
    expect(appPaneWidthForKeyboardResize("Escape", "collapsed", 650, 1000)).toBeNull();
  });

  test("keyboard output remains compatible with shared clamping policy", () => {
    const requestedTooSmall = appPaneWidthForKeyboardResize("ArrowRight", "expanded", 290, 1000);
    const requestedTooLarge = appPaneWidthForKeyboardResize("ArrowLeft", "collapsed", 674, 1000);

    expect(clampAppPaneWidth("expanded", requestedTooSmall, 1000)).toBe(288);
    expect(clampAppPaneWidth("collapsed", requestedTooLarge, 1000)).toBe(680);
  });
});
