import { describe, expect, test } from "bun:test";
import { ShellLayoutController } from "./controller";

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

function createAsyncMemoryStorage(snapshot = null) {
  const saves = [];

  return {
    saves,
    storage: {
      load: () => null,
      hydrate: async () => snapshot,
      save: (next) => saves.push(next)
    }
  };
}

describe("shell layout controller", () => {
  test("creates the expanded default layout after measurement", () => {
    const controller = new ShellLayoutController();
    const layout = controller.measure(1000);

    expect(layout.mode).toBe("expanded");
    expect(layout.appPaneWidth).toBe(450);
    expect(layout.workspacePaneWidth).toBe(550);
    expect(layout.appPaneShare).toBe(0.45);
  });

  test("creates the collapsed default layout after measurement", () => {
    const controller = new ShellLayoutController({ initialSnapshot: { sidebarCollapsed: true } });
    const layout = controller.measure(1000);

    expect(layout.mode).toBe("collapsed");
    expect(layout.appPaneWidth).toBe(650);
    expect(layout.workspacePaneWidth).toBe(350);
    expect(layout.appPaneShare).toBe(0.65);
  });

  test("remembers app pane widths separately by shell mode", () => {
    const controller = new ShellLayoutController();

    expect(controller.measure(1000).appPaneWidth).toBe(450);
    expect(controller.resizeAppPane(320).appPaneWidth).toBe(320);

    expect(controller.setSidebarCollapsed(true).appPaneWidth).toBe(650);
    expect(controller.resizeAppPane(500).appPaneWidth).toBe(500);

    expect(controller.setSidebarCollapsed(false).appPaneWidth).toBe(320);
    expect(controller.setSidebarCollapsed(true).appPaneWidth).toBe(500);
  });

  test("resizes from a frozen drag geometry", () => {
    const controller = new ShellLayoutController();
    controller.measure(1000);

    const layout = controller.resizeFromClientX(1200, 700, "expanded", 1000);

    expect(layout.appPaneWidth).toBe(500);
    expect(layout.workspacePaneWidth).toBe(500);
  });

  test("keyboard resize uses the active mode and current measured width", () => {
    const controller = new ShellLayoutController({ initialSnapshot: { sidebarCollapsed: true } });
    controller.measure(1000);

    expect(controller.keyboardResize("ArrowRight").appPaneWidth).toBe(626);
    expect(controller.keyboardResize("Enter").appPaneWidth).toBe(650);
  });

  test("ignores layout changes before a useful measurement exists", () => {
    const controller = new ShellLayoutController();

    expect(controller.currentLayout()).toBeNull();
    expect(controller.resizeAppPane(400)).toBeNull();
    expect(controller.keyboardResize("ArrowLeft")).toBeNull();
  });

  test("loads remembered desktop shell intent from storage", () => {
    const { storage } = createMemoryStorage({
      sidebarCollapsed: true,
      expandedAppPaneWidth: 320,
      collapsedAppPaneWidth: 520
    });
    const controller = new ShellLayoutController({ storage });

    expect(controller.measure(1000).appPaneWidth).toBe(520);
    expect(controller.setSidebarCollapsed(false, false).appPaneWidth).toBe(320);
  });

  test("marks async desktop hydration before first visible shell paint", async () => {
    const { storage } = createAsyncMemoryStorage({
      sidebarCollapsed: true,
      expandedAppPaneWidth: 320,
      collapsedAppPaneWidth: 520
    });
    const controller = new ShellLayoutController({ storage });

    expect(controller.hydrated).toBe(false);
    expect(controller.stateSnapshot.hydrated).toBe(false);

    await controller.hydrate();

    expect(controller.hydrated).toBe(true);
    expect(controller.stateSnapshot).toEqual({
      sidebarCollapsed: true,
      expandedAppPaneWidth: 320,
      collapsedAppPaneWidth: 520,
      hydrated: true
    });
  });

  test("marks async desktop hydration even when storage is unavailable", async () => {
    const controller = new ShellLayoutController({
      storage: {
        load: () => null,
        hydrate: async () => {
          throw new Error("unavailable");
        },
        save: () => {}
      }
    });

    await expect(controller.hydrate()).rejects.toThrow("unavailable");
    expect(controller.hydrated).toBe(true);
  });

  test("persists committed shell intent without persisting measured layout", () => {
    const { storage, saves } = createMemoryStorage();
    const controller = new ShellLayoutController({ storage });

    controller.measure(1000);
    controller.resizeAppPane(430);
    expect(saves).toHaveLength(0);

    controller.commit();
    expect(saves).toEqual([
      {
        sidebarCollapsed: false,
        expandedAppPaneWidth: 430,
        collapsedAppPaneWidth: null
      }
    ]);

    controller.toggleSidebar();
    expect(saves.at(-1)).toEqual({
      sidebarCollapsed: true,
      expandedAppPaneWidth: 430,
      collapsedAppPaneWidth: null
    });
  });
});
