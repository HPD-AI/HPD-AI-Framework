import { describe, expect, test } from "bun:test";
import { ShellController } from "./controller";

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

describe("shell controller", () => {
  test("starts on the chat route with the sidebar expanded", () => {
    const controller = new ShellController();

    expect(controller.stateSnapshot).toEqual({
      activeRoute: "chat",
      sidebarCollapsed: false,
      hydrated: true
    });
  });

  test("changes route without touching sidebar state", () => {
    const controller = new ShellController();

    controller.setRoute("settings");
    expect(controller.stateSnapshot.activeRoute).toBe("settings");
    expect(controller.stateSnapshot.sidebarCollapsed).toBe(false);
  });

  test("toggles sidebar without touching active route", () => {
    const controller = new ShellController({ initialSnapshot: { activeRoute: "automations" } });

    controller.toggleSidebar();
    expect(controller.stateSnapshot.activeRoute).toBe("automations");
    expect(controller.stateSnapshot.sidebarCollapsed).toBe(true);
  });

  test("loads remembered shell state from storage", () => {
    const { storage } = createMemoryStorage({
      activeRoute: "settings",
      sidebarCollapsed: true
    });
    const controller = new ShellController({ storage });

    expect(controller.stateSnapshot).toEqual({
      activeRoute: "settings",
      sidebarCollapsed: true,
      hydrated: true
    });
  });

  test("marks async desktop hydration before first visible shell paint", async () => {
    const { storage } = createAsyncMemoryStorage({
      activeRoute: "automations",
      sidebarCollapsed: true
    });
    const controller = new ShellController({ storage });

    expect(controller.hydrated).toBe(false);
    expect(controller.stateSnapshot.hydrated).toBe(false);

    await controller.hydrate();

    expect(controller.hydrated).toBe(true);
    expect(controller.stateSnapshot).toEqual({
      activeRoute: "automations",
      sidebarCollapsed: true,
      hydrated: true
    });
  });

  test("persists committed shell state", () => {
    const { storage, saves } = createMemoryStorage();
    const controller = new ShellController({ storage });

    controller.setRoute("settings");
    controller.toggleSidebar();

    expect(saves).toEqual([
      {
        activeRoute: "settings",
        sidebarCollapsed: false
      },
      {
        activeRoute: "settings",
        sidebarCollapsed: true
      }
    ]);
  });
});
