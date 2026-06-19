import {
  layout,
  prepare,
} from '@chenglou/pretext';

export interface ThreadComposerPretextOptions {
  font?: string;
  lineHeight?: number;
  letterSpacing?: number;
}

export interface ThreadComposerAutosizeMetrics {
  borderBlock: number;
  contentWidth: number;
  font: string;
  letterSpacing: number;
  lineHeight: number;
  paddingBlock: number;
}

export interface ThreadComposerAutosizeContext {
  maxRows: number;
  metrics: ThreadComposerAutosizeMetrics;
  minRows: number;
  node: HTMLTextAreaElement;
  value: string;
}

export interface ThreadComposerAutosizeResult {
  height: number;
  lineCount: number;
  rows: number;
}

export type ThreadComposerAutosizeStrategy =
  | false
  | 'pretext'
  | ((context: ThreadComposerAutosizeContext) => number | null | undefined);

export function readTextareaAutosizeMetrics(
  node: HTMLTextAreaElement,
  options: ThreadComposerPretextOptions | undefined,
): ThreadComposerAutosizeMetrics | null {
  const style = getComputedStyle(node);
  const font = options?.font ?? style.font;
  const lineHeight = options?.lineHeight ?? parseCssNumber(style.lineHeight);
  const letterSpacing = options?.letterSpacing ?? parseCssNumber(style.letterSpacing) ?? 0;

  if (!font || lineHeight === null || !Number.isFinite(lineHeight) || lineHeight <= 0) {
    return null;
  }

  const paddingTop = parseCssNumber(style.paddingTop) ?? 0;
  const paddingBottom = parseCssNumber(style.paddingBottom) ?? 0;
  const paddingLeft = parseCssNumber(style.paddingLeft) ?? 0;
  const paddingRight = parseCssNumber(style.paddingRight) ?? 0;
  const borderTop = parseCssNumber(style.borderTopWidth) ?? 0;
  const borderBottom = parseCssNumber(style.borderBottomWidth) ?? 0;
  const contentWidth = Math.max(0, node.clientWidth - paddingLeft - paddingRight);

  return {
    borderBlock: borderTop + borderBottom,
    contentWidth,
    font,
    letterSpacing,
    lineHeight,
    paddingBlock: paddingTop + paddingBottom,
  };
}

export function applyThreadComposerAutosize(
  node: HTMLTextAreaElement,
  value: string,
  autosize: ThreadComposerAutosizeStrategy,
  metrics: ThreadComposerAutosizeMetrics | null,
  minRows: number,
  maxRows: number,
): ThreadComposerAutosizeResult | null {
  if (autosize === false || metrics === null || metrics.contentWidth <= 0) {
    return null;
  }

  const normalizedMinRows = Math.max(1, Math.floor(minRows));
  const normalizedMaxRows = Math.max(normalizedMinRows, Math.floor(maxRows));

  if (typeof autosize === 'function') {
    const height = autosize({
      maxRows: normalizedMaxRows,
      metrics,
      minRows: normalizedMinRows,
      node,
      value,
    });
    if (height === null || height === undefined || !Number.isFinite(height)) return null;
    node.style.height = `${height}px`;
    return {
      height,
      lineCount: Math.max(1, Math.round((height - metrics.paddingBlock - metrics.borderBlock) / metrics.lineHeight)),
      rows: Math.max(1, Math.round((height - metrics.paddingBlock - metrics.borderBlock) / metrics.lineHeight)),
    };
  }

  try {
    const prepared = prepare(value, metrics.font, {
      whiteSpace: 'pre-wrap',
      letterSpacing: metrics.letterSpacing,
    });
    const result = layout(prepared, metrics.contentWidth, metrics.lineHeight);
    const lineCount = Math.max(1, result.lineCount);
    const rows = clamp(lineCount, normalizedMinRows, normalizedMaxRows);
    const height = rows * metrics.lineHeight + metrics.paddingBlock + metrics.borderBlock;
    node.style.height = `${height}px`;
    return { height, lineCount, rows };
  } catch {
    return null;
  }
}

function parseCssNumber(value: string): number | null {
  const parsed = Number.parseFloat(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}
