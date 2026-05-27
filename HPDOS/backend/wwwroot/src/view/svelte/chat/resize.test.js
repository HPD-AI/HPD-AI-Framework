import { describe, expect, test } from "bun:test";
import { appPaneWidthForKeyboardResize } from "./controller";

describe("chat resize keyboard policy", () => {
  test("arrow keys resize the app pane from the divider perspective", () => {
    expect(appPaneWidthForKeyboardResize("ArrowLeft", "expanded", 400, 1000)).toBe(424);
    expect(appPaneWidthForKeyboardResize("ArrowRight", "expanded", 400, 1000)).toBe(376);
  });

  test("shift arrow keys use the larger resize step", () => {
    expect(appPaneWidthForKeyboardResize("ArrowLeft", "expanded", 400, 1000, true)).toBe(496);
    expect(appPaneWidthForKeyboardResize("ArrowRight", "expanded", 400, 1000, true)).toBe(304);
  });

  test("home and end target the legal app pane bounds", () => {
    expect(appPaneWidthForKeyboardResize("Home", "expanded", 400, 1000)).toBe(288);
    expect(appPaneWidthForKeyboardResize("End", "expanded", 400, 1000)).toBe(584);
  });

  test("enter resets to the current mode default", () => {
    expect(appPaneWidthForKeyboardResize("Enter", "expanded", 400, 1000)).toBe(450);
    expect(appPaneWidthForKeyboardResize("Enter", "collapsed", 400, 1000)).toBe(650);
  });

  test("unsupported keys are ignored", () => {
    expect(appPaneWidthForKeyboardResize("Escape", "expanded", 400, 1000)).toBeNull();
  });

  test("home and end expose the shared legal bounds directly", () => {
    expect(appPaneWidthForKeyboardResize("Home", "expanded", 290, 1000)).toBe(288);
    expect(appPaneWidthForKeyboardResize("End", "expanded", 580, 1000)).toBe(584);
  });
});
