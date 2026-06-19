import { diffLines, diffWordsWithSpace } from 'diff';
import parseDiff from 'parse-diff';
import type {
  DiffDisplayLine,
  DiffFile,
  DiffFileInput,
  DiffFold,
  DiffIntraLineSegments,
  DiffLine,
  DiffSegment,
  DiffSplitLinePair,
} from './types.js';

const NO_NEWLINE_MARKER = '\\ No newline at end of file';

export function parseDiffPatch(patch: string): DiffFile[] {
  return parseDiff(patch).map((file) => {
    const lines: DiffLine[] = [];
    let additions = 0;
    let deletions = 0;

    for (const chunk of file.chunks) {
      let oldLine = chunk.oldStart;
      let newLine = chunk.newStart;

      for (const change of chunk.changes) {
        const content = parseChangeContent(change.content);
        if (content === null) continue;

        if (change.type === 'add') {
          additions++;
          lines.push({
            type: 'add',
            content,
            newLineNumber: newLine++,
          });
        } else if (change.type === 'del') {
          deletions++;
          lines.push({
            type: 'del',
            content,
            oldLineNumber: oldLine++,
          });
        } else {
          lines.push({
            type: 'normal',
            content,
            oldLineNumber: oldLine++,
            newLineNumber: newLine++,
          });
        }
      }
    }

    return {
      oldName: file.from,
      newName: file.to,
      lines,
      additions,
      deletions,
    };
  });
}

export function computeTextDiff(oldFile: DiffFileInput, newFile: DiffFileInput): DiffFile {
  const lines: DiffLine[] = [];
  let oldLine = 1;
  let newLine = 1;
  let additions = 0;
  let deletions = 0;

  for (const change of diffLines(oldFile.content, newFile.content)) {
    for (const content of splitDiffLines(change.value)) {
      if (change.added) {
        additions++;
        lines.push({
          type: 'add',
          content,
          newLineNumber: newLine++,
        });
      } else if (change.removed) {
        deletions++;
        lines.push({
          type: 'del',
          content,
          oldLineNumber: oldLine++,
        });
      } else {
        lines.push({
          type: 'normal',
          content,
          oldLineNumber: oldLine++,
          newLineNumber: newLine++,
        });
      }
    }
  }

  return {
    oldName: oldFile.name,
    newName: newFile.name,
    lines,
    additions,
    deletions,
  };
}

export function getDiffFiles(input: {
  patch?: string;
  oldFile?: DiffFileInput;
  newFile?: DiffFileInput;
}): DiffFile[] {
  if (input.patch) return parseDiffPatch(input.patch);
  if (input.oldFile && input.newFile) return [computeTextDiff(input.oldFile, input.newFile)];
  return [];
}

export function foldDiffContext(lines: DiffLine[], contextLines: number): DiffDisplayLine[] {
  const context = Math.max(0, contextLines);
  const keep = new Set<number>();

  for (let index = 0; index < lines.length; index++) {
    if (lines[index]?.type === 'normal') continue;

    for (
      let keepIndex = Math.max(0, index - context);
      keepIndex <= Math.min(lines.length - 1, index + context);
      keepIndex++
    ) {
      keep.add(keepIndex);
    }
  }

  const displayLines: DiffDisplayLine[] = [];
  let index = 0;

  while (index < lines.length) {
    if (keep.has(index)) {
      displayLines.push(lines[index]!);
      index++;
      continue;
    }

    let hiddenCount = 0;
    while (index < lines.length && !keep.has(index)) {
      hiddenCount++;
      index++;
    }

    displayLines.push({
      type: 'fold',
      hiddenCount,
    } satisfies DiffFold);
  }

  return displayLines;
}

export function getDiffDisplayLines(options: {
  lines: DiffLine[];
  contextLines?: number;
  maxLines?: number;
}): {
  lines: DiffDisplayLine[];
  truncated: boolean;
  remainingCount: number;
} {
  const displayLines = options.contextLines === undefined
    ? options.lines
    : foldDiffContext(options.lines, options.contextLines);

  if (options.maxLines === undefined || displayLines.length <= options.maxLines) {
    return {
      lines: displayLines,
      truncated: false,
      remainingCount: 0,
    };
  }

  return {
    lines: displayLines.slice(0, options.maxLines),
    truncated: true,
    remainingCount: displayLines.length - options.maxLines,
  };
}

export function pairDiffLinesForSplit(lines: DiffLine[]): DiffSplitLinePair[] {
  const pairs: DiffSplitLinePair[] = [];
  let index = 0;

  while (index < lines.length) {
    const line = lines[index]!;

    if (line.type === 'normal') {
      pairs.push({ left: line, right: line });
      index++;
      continue;
    }

    if (line.type === 'del') {
      const deletions: DiffLine[] = [];
      while (lines[index]?.type === 'del') {
        deletions.push(lines[index]!);
        index++;
      }

      const additions: DiffLine[] = [];
      while (lines[index]?.type === 'add') {
        additions.push(lines[index]!);
        index++;
      }

      const length = Math.max(deletions.length, additions.length);
      for (let pairIndex = 0; pairIndex < length; pairIndex++) {
        pairs.push({
          left: deletions[pairIndex] ?? null,
          right: additions[pairIndex] ?? null,
        });
      }
      continue;
    }

    pairs.push({ left: null, right: line });
    index++;
  }

  return pairs;
}

export function buildDiffLinePairMap(lines: DiffLine[]): Map<DiffLine, DiffLine> {
  const pairMap = new Map<DiffLine, DiffLine>();

  for (let index = 0; index < lines.length; index++) {
    if (lines[index]?.type !== 'del') continue;

    const deletionEnd = getRunEnd(lines, index, 'del');
    const additionEnd = getRunEnd(lines, deletionEnd, 'add');
    const deletionLength = deletionEnd - index;
    const additionLength = additionEnd - deletionEnd;

    if (deletionLength > 0 && deletionLength === additionLength) {
      for (let offset = 0; offset < deletionLength; offset++) {
        pairMap.set(lines[index + offset]!, lines[deletionEnd + offset]!);
      }
      index = additionEnd - 1;
      continue;
    }

    index = deletionEnd - 1;
  }

  return pairMap;
}

export function buildIntraLineDiffSegments(
  delText: string,
  addText: string,
): DiffIntraLineSegments {
  const delSegments: DiffSegment[] = [];
  const addSegments: DiffSegment[] = [];

  for (const part of diffWordsWithSpace(delText, addText)) {
    if (!part.added) {
      delSegments.push({
        text: part.value,
        changed: Boolean(part.removed),
      });
    }

    if (!part.removed) {
      addSegments.push({
        text: part.value,
        changed: Boolean(part.added),
      });
    }
  }

  return {
    delSegments,
    addSegments,
  };
}

function getRunEnd(lines: DiffLine[], startIndex: number, type: DiffLine['type']): number {
  let index = startIndex;
  while (lines[index]?.type === type) index++;
  return index;
}

function parseChangeContent(content: string): string | null {
  const normalized = stripTrailingCarriageReturn(content);
  if (normalized === NO_NEWLINE_MARKER) return null;
  return stripTrailingCarriageReturn(normalized.slice(1));
}

function splitDiffLines(value: string): string[] {
  const normalized = value.endsWith('\n') ? value.slice(0, -1) : value;
  if (normalized.length === 0) {
    return value.length > 0 ? [''] : [];
  }

  return normalized.split('\n').map(stripTrailingCarriageReturn);
}

function stripTrailingCarriageReturn(content: string): string {
  return content.endsWith('\r') ? content.slice(0, -1) : content;
}
