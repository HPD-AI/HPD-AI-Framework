import type { Attachment } from "svelte/attachments";
import { on } from "svelte/events";
import { ChatLayoutController, type ChatPaneLayout } from "./controller";
import type { ChatLayoutMode } from "./layout";

type DragGeometry = {
  mode: ChatLayoutMode;
  routeRight: number;
  resizableWidth: number;
};

export function attachChatResize(
  controller: ChatLayoutController,
  mode: () => ChatLayoutMode
): Attachment<HTMLElement> {
  return (resizeHandle) => {
    const routeElement = resizeHandle.closest(".hpd-chat-route");
    if (!(routeElement instanceof HTMLElement)) return;

    let stopDragListeners: (() => void) | null = null;
    let dragGeometry: DragGeometry | null = null;
    let pendingClientX: number | null = null;
    let animationFrame = 0;
    let layoutFrame = 0;
    let lastWorkspacePaneWidth = -1;
    let lastAppPaneWidth = -1;

    const applyLayout = (layout: ChatPaneLayout | null, updateSemantics = true): boolean => {
      if (layout === null) return false;

      const roundedWorkspacePaneWidth = Math.round(layout.workspacePaneWidth);
      const roundedAppPaneWidth = Math.round(layout.appPaneWidth);

      if (
        roundedWorkspacePaneWidth === lastWorkspacePaneWidth
        && roundedAppPaneWidth === lastAppPaneWidth
      ) {
        if (updateSemantics) updateResizeSemantics(layout);
        return true;
      }

      lastWorkspacePaneWidth = roundedWorkspacePaneWidth;
      lastAppPaneWidth = roundedAppPaneWidth;
      routeElement.style.setProperty("--hpd-workspace-pane-width", `${roundedWorkspacePaneWidth}px`);
      routeElement.style.setProperty("--hpd-app-pane-width", `${roundedAppPaneWidth}px`);

      if (updateSemantics) updateResizeSemantics(layout);

      return true;
    };

    const updateResizeSemantics = (layout: ChatPaneLayout): void => {
      const roundedAppPaneWidth = Math.round(layout.appPaneWidth);
      const appPaneShare = Math.round(layout.appPaneShare * 100);

      resizeHandle.setAttribute("aria-valuemin", `${Math.round(layout.minAppPaneWidth)}`);
      resizeHandle.setAttribute("aria-valuemax", `${Math.round(layout.maxAppPaneWidth)}`);
      resizeHandle.setAttribute("aria-valuenow", `${roundedAppPaneWidth}`);
      resizeHandle.setAttribute("aria-valuetext", `App section ${appPaneShare}%`);
    };

    const applyCurrentLayout = (): boolean => {
      return applyLayout(controller.measure(readResizableWidth(), mode()));
    };

    const scheduleLayout = (): void => {
      if (layoutFrame !== 0) {
        cancelAnimationFrame(layoutFrame);
        layoutFrame = 0;
      }

      applyCurrentLayout();
    };

    const readResizableWidth = (): number => {
      const routeRect = routeElement.getBoundingClientRect();
      const routeStyles = getComputedStyle(routeElement);
      const gap = Number.parseFloat(routeStyles.columnGap) || 0;

      return Math.max(1, routeRect.width - gap);
    };

    const readDragGeometry = (): DragGeometry => {
      const routeRect = routeElement.getBoundingClientRect();

      return {
        mode: mode(),
        routeRight: routeRect.right,
        resizableWidth: readResizableWidth()
      };
    };

    const updateAppPaneWidth = (geometry: DragGeometry, clientX: number): void => {
      applyLayout(
        controller.resizeFromClientX(
          geometry.routeRight,
          clientX,
          geometry.mode,
          geometry.resizableWidth
        ),
        false
      );
    };

    const flushPendingDrag = (): void => {
      animationFrame = 0;
      if (dragGeometry === null || pendingClientX === null) return;

      updateAppPaneWidth(dragGeometry, pendingClientX);
    };

    const scheduleDrag = (clientX: number): void => {
      pendingClientX = clientX;
      if (animationFrame !== 0) return;

      animationFrame = requestAnimationFrame(flushPendingDrag);
    };

    const stopDrag = (): void => {
      const shouldCommit = dragGeometry !== null;
      if (dragGeometry !== null && pendingClientX !== null) {
        updateAppPaneWidth(dragGeometry, pendingClientX);
      }
      stopDragListeners?.();
      stopDragListeners = null;
      dragGeometry = null;
      pendingClientX = null;
      if (animationFrame !== 0) {
        cancelAnimationFrame(animationFrame);
        animationFrame = 0;
      }
      delete document.body.dataset.hpdPaneResizing;
      applyCurrentLayout();
      if (shouldCommit) controller.commit();
    };

    const startDrag = (event: PointerEvent): void => {
      if (event.button !== 0) return;

      event.preventDefault();
      event.stopPropagation();

      stopDrag();
      if (layoutFrame !== 0) {
        cancelAnimationFrame(layoutFrame);
        layoutFrame = 0;
      }

      dragGeometry = readDragGeometry();
      document.body.dataset.hpdPaneResizing = "true";
      resizeHandle.setPointerCapture?.(event.pointerId);
      updateAppPaneWidth(dragGeometry, event.clientX);

      const stopPointerMove = on(
        window,
        "pointermove",
        (moveEvent) => scheduleDrag(moveEvent.clientX),
        { capture: true }
      );
      const stopPointerUp = on(window, "pointerup", stopDrag, { capture: true });
      const stopPointerCancel = on(window, "pointercancel", stopDrag, { capture: true });

      stopDragListeners = (): void => {
        stopPointerMove();
        stopPointerUp();
        stopPointerCancel();
      };
    };

    const handleKeydown = (event: KeyboardEvent): void => {
      controller.measure(readResizableWidth(), mode());
      const layout = controller.keyboardResize(event.key, mode(), event.shiftKey);
      if (layout === null) return;

      event.preventDefault();
      event.stopPropagation();
      applyLayout(layout);
    };

    const stopPointerDown = on(resizeHandle, "pointerdown", startDrag, { capture: true });
    const stopKeydown = on(resizeHandle, "keydown", handleKeydown);
    const resizeObserver = new ResizeObserver(scheduleLayout);
    resizeObserver.observe(routeElement);
    const stopStateSubscription = controller.state.subscribe(scheduleLayout);

    return () => {
      stopDrag();
      stopPointerDown();
      stopKeydown();
      stopStateSubscription();
      resizeObserver.disconnect();
      if (layoutFrame !== 0) {
        cancelAnimationFrame(layoutFrame);
        layoutFrame = 0;
      }
    };
  };
}
