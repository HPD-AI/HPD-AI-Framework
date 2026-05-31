import { describe, expect, test } from "bun:test";
import { ChatLayoutController } from "./controller";

function createMemoryStorage(snapshot = null) {
  const saves = [];

  return {
    saves,
    storage: {
      load: () => snapshot,
      save: (next) => saves.push(next)
    }
  };
}

describe("chat layout controller", () => {
  test("creates the expanded default layout after measurement", () => {
    const controller = new ChatLayoutController();
    const layout = controller.measure(1000, "expanded");

    expect(layout.mode).toBe("expanded");
    expect(layout.appPaneWidth).toBe(450);
    expect(layout.workspacePaneWidth).toBe(550);
    expect(layout.appPaneShare).toBe(0.45);
  });

  test("creates the collapsed default layout after measurement", () => {
    const controller = new ChatLayoutController();
    const layout = controller.measure(1000, "collapsed");

    expect(layout.mode).toBe("collapsed");
    expect(layout.appPaneWidth).toBe(650);
    expect(layout.workspacePaneWidth).toBe(350);
    expect(layout.appPaneShare).toBe(0.65);
  });

  test("remembers app pane widths separately by sidebar mode", () => {
    const controller = new ChatLayoutController();

    expect(controller.measure(1000, "expanded").appPaneWidth).toBe(450);
    expect(controller.resizeAppPane(320, "expanded").appPaneWidth).toBe(320);

    expect(controller.measure(1000, "collapsed").appPaneWidth).toBe(650);
    expect(controller.resizeAppPane(500, "collapsed").appPaneWidth).toBe(500);

    expect(controller.measure(1000, "expanded").appPaneWidth).toBe(320);
    expect(controller.measure(1000, "collapsed").appPaneWidth).toBe(500);
  });

  test("resizes from a frozen drag geometry", () => {
    const controller = new ChatLayoutController();
    controller.measure(1000, "expanded");

    const layout = controller.resizeFromClientX(1200, 700, "expanded", 1000);

    expect(layout.appPaneWidth).toBe(500);
    expect(layout.workspacePaneWidth).toBe(500);
  });

  test("keyboard resize uses the active mode and current measured width", () => {
    const controller = new ChatLayoutController();
    controller.measure(1000, "collapsed");

    expect(controller.keyboardResize("ArrowRight", "collapsed").appPaneWidth).toBe(626);
    expect(controller.keyboardResize("Enter", "collapsed").appPaneWidth).toBe(650);
  });

  test("ignores layout changes before a useful measurement exists", () => {
    const controller = new ChatLayoutController();

    expect(controller.currentLayout("expanded")).toBeNull();
    expect(controller.resizeAppPane(400, "expanded")).toBeNull();
    expect(controller.keyboardResize("ArrowLeft", "expanded")).toBeNull();
  });

  test("loads remembered chat layout state from storage", () => {
    const { storage } = createMemoryStorage({
      chatSectionCollapsed: true,
      expandedAppPaneWidth: 320,
      collapsedAppPaneWidth: 520
    });
    const controller = new ChatLayoutController({ storage });

    expect(controller.chatSectionCollapsed).toBe(true);
    expect(controller.measure(1000, "collapsed").appPaneWidth).toBe(520);
    expect(controller.measure(1000, "expanded").appPaneWidth).toBe(320);
  });

  test("toggles and persists the chat section collapsed state", () => {
    const { storage, saves } = createMemoryStorage();
    const controller = new ChatLayoutController({ storage });

    controller.toggleChatSection();

    expect(controller.chatSectionCollapsed).toBe(true);
    expect(saves).toEqual([
      {
        chatSectionCollapsed: true,
        expandedAppPaneWidth: null,
        collapsedAppPaneWidth: null
      }
    ]);
  });

  test("persists committed chat layout without sidebar or route state", () => {
    const { storage, saves } = createMemoryStorage();
    const controller = new ChatLayoutController({ storage });

    controller.measure(1000, "expanded");
    controller.resizeAppPane(430, "expanded");
    expect(saves).toHaveLength(0);

    controller.commit();
    expect(saves).toEqual([
      {
        chatSectionCollapsed: false,
        expandedAppPaneWidth: 430,
        collapsedAppPaneWidth: null
      }
    ]);
  });
});
