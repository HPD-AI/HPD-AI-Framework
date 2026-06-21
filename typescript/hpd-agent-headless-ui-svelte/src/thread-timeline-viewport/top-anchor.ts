export interface TopAnchorClampPixels {
  tallerThan: number;
  visibleHeight: number;
}

export interface ComputeTopAnchorTargetOptions extends TopAnchorClampPixels {
  anchor: HTMLElement;
  viewport: HTMLElement;
}

export interface ComputeTopAnchorReserveOptions extends ComputeTopAnchorTargetOptions {
  reserveHeight: number;
}

export function parseCssLength(value: string | undefined, element: HTMLElement): number {
  if (!value) return Number.POSITIVE_INFINITY;
  const match = value.trim().match(/^(\d+(?:\.\d+)?|\.\d+)(em|px|rem)$/);
  if (!match) return Number.POSITIVE_INFINITY;

  const amount = Number(match[1]);
  const unit = match[2];
  if (unit === 'px') return amount;
  if (unit === 'em') return amount * (parseFloat(getComputedStyle(element).fontSize) || 16);
  if (unit === 'rem') {
    return amount * (parseFloat(getComputedStyle(document.documentElement).fontSize) || 16);
  }

  return Number.POSITIVE_INFINITY;
}

export function getLayoutOffsetTop(element: HTMLElement, ancestor: HTMLElement): number {
  let top = 0;
  let current: HTMLElement | null = element;

  while (current && current !== ancestor) {
    top += current.offsetTop;
    current = current.offsetParent as HTMLElement | null;
  }

  if (current === ancestor) return top;

  const documentOffset = getDocumentOffsetTop(element) - getDocumentOffsetTop(ancestor);
  if (documentOffset !== 0) return documentOffset;

  const elementRect = element.getBoundingClientRect();
  const ancestorRect = ancestor.getBoundingClientRect();
  return ancestor.scrollTop + elementRect.top - ancestorRect.top;
}

export function computeTopAnchorTargetScrollTop(options: ComputeTopAnchorTargetOptions): number {
  const anchorTop = getLayoutOffsetTop(options.anchor, options.viewport);
  const anchorHeight = options.anchor.offsetHeight;
  const visibleAnchorHeight = anchorHeight <= options.tallerThan
    ? anchorHeight
    : options.visibleHeight;

  return snapScrollTop(anchorTop + Math.max(0, anchorHeight - visibleAnchorHeight));
}

export function computeTopAnchorReserve(options: ComputeTopAnchorReserveOptions): number {
  const targetScrollTop = computeTopAnchorTargetScrollTop(options);
  const reachableScrollHeight = options.viewport.scrollHeight - options.reserveHeight;
  const targetScrollHeight = targetScrollTop + options.viewport.clientHeight;
  return Math.max(0, targetScrollHeight - reachableScrollHeight);
}

export function snapScrollTop(top: number): number {
  const pixelRatio = window.devicePixelRatio || 1;
  return Math.round(top * pixelRatio) / pixelRatio;
}

function getDocumentOffsetTop(element: HTMLElement): number {
  let top = 0;
  let current: HTMLElement | null = element;

  while (current) {
    top += current.offsetTop;
    current = current.offsetParent as HTMLElement | null;
  }

  return top;
}
