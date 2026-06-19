import {
  buildDiffLinePairMap,
  buildIntraLineDiffSegments,
  getDiffDisplayLines,
  getDiffFiles,
  pairDiffLinesForSplit,
  type DiffDisplayLine,
  type DiffFile,
  type DiffFileInput,
  type DiffFold,
  type DiffLine,
  type DiffSegment,
  type DiffSplitLinePair,
} from '@hpd-research/hpd-agent-headless-ui';
import { mergeProps } from '../thread-composer/index.js';
import type {
  DiffViewerContentChildProps,
  DiffViewerContentElementProps,
  DiffViewerElementProps,
  DiffViewerFileChildProps,
  DiffViewerFileElementProps,
  DiffViewerFoldChildProps,
  DiffViewerFoldElementProps,
  DiffViewerHeaderChildProps,
  DiffViewerHeaderElementProps,
  DiffViewerLineChildProps,
  DiffViewerLineElementProps,
  DiffViewerModel,
  DiffViewerSegmentElementProps,
  DiffViewerSize,
  DiffViewerSplitLineChildProps,
  DiffViewerSplitLineElementProps,
  DiffViewerSplitSideElementProps,
  DiffViewerStatsChildProps,
  DiffViewerStatsElementProps,
  DiffViewerVariant,
  DiffViewerViewMode,
} from './types.js';

export function createDiffViewerModel(input: {
  newFile?: DiffFileInput;
  oldFile?: DiffFileInput;
  patch?: string;
  size?: DiffViewerSize;
  variant?: DiffViewerVariant;
  viewMode?: DiffViewerViewMode;
}): DiffViewerModel {
  const files = getDiffFiles(input);

  return {
    files,
    hasContent: files.length > 0,
    size: input.size ?? 'default',
    variant: input.variant ?? 'default',
    viewMode: input.viewMode ?? 'unified',
  };
}

export function createDiffViewerElementProps(
  model: DiffViewerModel,
  restProps: Record<string, unknown> = {},
): DiffViewerElementProps {
  return mergeProps(restProps, {
    'data-empty': model.hasContent ? undefined : '',
    'data-hpd-diff-viewer': '',
    'data-size': model.size,
    'data-variant': model.variant,
    'data-view-mode': model.viewMode,
  }) as unknown as DiffViewerElementProps;
}

export function createDiffViewerFileElementProps(
  fileIndex: number,
  restProps: Record<string, unknown> = {},
): DiffViewerFileElementProps {
  return mergeProps(restProps, {
    'data-file-index': fileIndex,
    'data-hpd-diff-file': '',
  }) as unknown as DiffViewerFileElementProps;
}

export function createDiffViewerHeaderElementProps(
  fileIndex: number,
  restProps: Record<string, unknown> = {},
): DiffViewerHeaderElementProps {
  return mergeProps(restProps, {
    'data-file-index': fileIndex,
    'data-hpd-diff-header': '',
  }) as unknown as DiffViewerHeaderElementProps;
}

export function createDiffViewerStatsElementProps(
  fileIndex: number,
  restProps: Record<string, unknown> = {},
): DiffViewerStatsElementProps {
  return mergeProps(restProps, {
    'data-file-index': fileIndex,
    'data-hpd-diff-stats': '',
  }) as unknown as DiffViewerStatsElementProps;
}

export function createDiffViewerContentElementProps(
  fileIndex: number,
  restProps: Record<string, unknown> = {},
): DiffViewerContentElementProps {
  return mergeProps(restProps, {
    'data-file-index': fileIndex,
    'data-hpd-diff-content': '',
  }) as unknown as DiffViewerContentElementProps;
}

export function createDiffViewerLineElementProps(
  line: DiffLine,
  restProps: Record<string, unknown> = {},
): DiffViewerLineElementProps {
  return mergeProps(restProps, {
    'data-hpd-diff-line': '',
    'data-line-type': line.type,
    'data-new-line-number': line.newLineNumber,
    'data-old-line-number': line.oldLineNumber,
  }) as unknown as DiffViewerLineElementProps;
}

export function createDiffViewerFoldElementProps(
  fold: DiffFold,
  restProps: Record<string, unknown> = {},
): DiffViewerFoldElementProps {
  return mergeProps(restProps, {
    'data-hidden-count': fold.hiddenCount,
    'data-hpd-diff-fold': '',
  }) as unknown as DiffViewerFoldElementProps;
}

export function createDiffViewerSegmentElementProps(
  segment: DiffSegment,
  restProps: Record<string, unknown> = {},
): DiffViewerSegmentElementProps {
  return mergeProps(restProps, {
    'data-changed': segment.changed ? '' : undefined,
    'data-hpd-diff-segment': '',
  }) as unknown as DiffViewerSegmentElementProps;
}

export function createDiffViewerSplitLineElementProps(
  restProps: Record<string, unknown> = {},
): DiffViewerSplitLineElementProps {
  return mergeProps(restProps, {
    'data-hpd-diff-split-line': '',
  }) as unknown as DiffViewerSplitLineElementProps;
}

export function createDiffViewerSplitSideElementProps(
  side: 'left' | 'right',
  line: DiffLine | null,
  restProps: Record<string, unknown> = {},
): DiffViewerSplitSideElementProps {
  return mergeProps(restProps, {
    'data-hpd-diff-split-side': '',
    'data-line-type': line?.type ?? 'empty',
    'data-side': side,
  }) as unknown as DiffViewerSplitSideElementProps;
}

export function createDiffViewerFileChildProps(
  file: DiffFile,
  fileIndex: number,
  restProps: Record<string, unknown> = {},
): DiffViewerFileChildProps {
  return {
    file,
    fileIndex,
    props: createDiffViewerFileElementProps(fileIndex, restProps),
  };
}

export function createDiffViewerHeaderChildProps(
  file: DiffFile,
  fileIndex: number,
  restProps: Record<string, unknown> = {},
): DiffViewerHeaderChildProps {
  const oldName = normalizeDevNull(file.oldName);
  const newName = normalizeDevNull(file.newName);
  const renamed = Boolean(oldName && newName && oldName !== newName);

  return {
    additions: file.additions,
    deletions: file.deletions,
    displayName: newName ?? oldName,
    file,
    fileIndex,
    oldName,
    newName,
    props: createDiffViewerHeaderElementProps(fileIndex, restProps),
    renamed,
  };
}

export function createDiffViewerStatsChildProps(
  file: DiffFile,
  fileIndex: number,
  restProps: Record<string, unknown> = {},
): DiffViewerStatsChildProps {
  return {
    additions: file.additions,
    deletions: file.deletions,
    file,
    fileIndex,
    props: createDiffViewerStatsElementProps(fileIndex, restProps),
  };
}

export function createDiffViewerContentChildProps(input: {
  contextLines?: number;
  file: DiffFile;
  fileIndex: number;
  maxLines?: number;
  restProps?: Record<string, unknown>;
}): DiffViewerContentChildProps {
  const display = getDiffDisplayLines({
    contextLines: input.contextLines,
    lines: input.file.lines,
    maxLines: input.maxLines,
  });

  return {
    displayLines: display.lines,
    file: input.file,
    fileIndex: input.fileIndex,
    props: createDiffViewerContentElementProps(input.fileIndex, input.restProps),
    remainingCount: display.remainingCount,
    splitPairs: pairDiffLinesForSplit(extractVisibleDiffLines(display.lines)),
    truncated: display.truncated,
  };
}

export function createDiffViewerLineChildProps(input: {
  file: DiffFile;
  fileIndex: number;
  index: number;
  line: DiffLine;
  restProps?: Record<string, unknown>;
  segments?: DiffSegment[] | null;
}): DiffViewerLineChildProps {
  return {
    file: input.file,
    fileIndex: input.fileIndex,
    index: input.index,
    line: input.line,
    props: createDiffViewerLineElementProps(input.line, input.restProps),
    segments: input.segments ?? null,
  };
}

export function createDiffViewerFoldChildProps(input: {
  file: DiffFile;
  fileIndex: number;
  fold: DiffFold;
  index: number;
  restProps?: Record<string, unknown>;
}): DiffViewerFoldChildProps {
  return {
    file: input.file,
    fileIndex: input.fileIndex,
    fold: input.fold,
    index: input.index,
    props: createDiffViewerFoldElementProps(input.fold, input.restProps),
  };
}

export function createDiffViewerSplitLineChildProps(input: {
  file: DiffFile;
  fileIndex: number;
  index: number;
  pair: DiffSplitLinePair;
  restProps?: Record<string, unknown>;
}): DiffViewerSplitLineChildProps {
  return {
    file: input.file,
    fileIndex: input.fileIndex,
    index: input.index,
    pair: input.pair,
    props: createDiffViewerSplitLineElementProps(input.restProps),
  };
}

export function createDiffViewerSegmentMap(lines: DiffLine[]): Map<DiffLine, DiffSegment[]> {
  const linePairs = buildDiffLinePairMap(lines);
  const segmentMap = new Map<DiffLine, DiffSegment[]>();

  for (const [deleted, added] of linePairs) {
    const segments = buildIntraLineDiffSegments(deleted.content, added.content);
    segmentMap.set(deleted, segments.delSegments);
    segmentMap.set(added, segments.addSegments);
  }

  return segmentMap;
}

export function getDiffLineNumber(line: DiffLine, side?: 'left' | 'right'): number | undefined {
  if (side === 'left') return line.oldLineNumber;
  if (side === 'right') return line.newLineNumber;
  if (line.type === 'add') return line.newLineNumber;
  return line.oldLineNumber;
}

export function getDiffLineIndicator(line: DiffLine | null, side?: 'left' | 'right'): string {
  if (!line) return '';
  if (line.type === 'add') return '+';
  if (line.type === 'del') return '-';
  if (side === 'left' || side === 'right') return ' ';
  return ' ';
}

export function getDiffFileExtension(filename: string | undefined): string {
  const extension = filename?.split('.').pop()?.toUpperCase();
  if (!extension || extension === filename?.toUpperCase()) return '';
  return extension;
}

function extractVisibleDiffLines(lines: DiffDisplayLine[]): DiffLine[] {
  return lines.filter((line): line is DiffLine => line.type !== 'fold');
}

function normalizeDevNull(name: string | undefined): string | undefined {
  if (!name || name === '/dev/null') return undefined;
  return name;
}
