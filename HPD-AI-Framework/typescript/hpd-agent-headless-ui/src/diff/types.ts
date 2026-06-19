export type DiffLineType = 'add' | 'del' | 'normal';

export interface DiffLine {
  type: DiffLineType;
  content: string;
  oldLineNumber?: number;
  newLineNumber?: number;
}

export interface DiffFold {
  type: 'fold';
  hiddenCount: number;
}

export type DiffDisplayLine = DiffLine | DiffFold;

export interface DiffFile {
  oldName?: string;
  newName?: string;
  lines: DiffLine[];
  additions: number;
  deletions: number;
}

export interface DiffFileInput {
  content: string;
  name?: string;
}

export interface DiffSplitLinePair {
  left: DiffLine | null;
  right: DiffLine | null;
}

export interface DiffSegment {
  text: string;
  changed: boolean;
}

export interface DiffIntraLineSegments {
  delSegments: DiffSegment[];
  addSegments: DiffSegment[];
}
