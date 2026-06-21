import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type {
  DiffDisplayLine,
  DiffFile,
  DiffFileInput,
  DiffFold,
  DiffLine,
  DiffSegment,
  DiffSplitLinePair,
} from '@hpd-research/hpd-agent-headless-ui';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;
type SpanProps = Omit<SvelteHTMLElements['span'], 'children'>;

export type DiffViewerViewMode = 'unified' | 'split';
export type DiffViewerSize = 'sm' | 'default' | 'lg';
export type DiffViewerVariant = 'default' | 'ghost' | 'muted';

export interface DiffViewerModel {
  files: DiffFile[];
  hasContent: boolean;
  size: DiffViewerSize;
  variant: DiffViewerVariant;
  viewMode: DiffViewerViewMode;
}

export interface DiffViewerContext {
  contextLines?: number;
  files: DiffFile[];
  maxLines?: number;
  showLineNumbers: boolean;
  viewMode: DiffViewerViewMode;
}

export type DiffViewerElementProps = DivProps & {
  'data-hpd-diff-viewer': '';
  'data-view-mode': DiffViewerViewMode;
  'data-variant': DiffViewerVariant;
  'data-size': DiffViewerSize;
  'data-empty'?: '';
};

export type DiffViewerFileElementProps = DivProps & {
  'data-hpd-diff-file': '';
  'data-file-index': number;
};

export type DiffViewerHeaderElementProps = DivProps & {
  'data-hpd-diff-header': '';
  'data-file-index': number;
};

export type DiffViewerStatsElementProps = SpanProps & {
  'data-hpd-diff-stats': '';
  'data-file-index': number;
};

export type DiffViewerContentElementProps = DivProps & {
  'data-hpd-diff-content': '';
  'data-file-index': number;
};

export type DiffViewerLineElementProps = DivProps & {
  'data-hpd-diff-line': '';
  'data-line-type': DiffLine['type'];
  'data-old-line-number'?: number;
  'data-new-line-number'?: number;
};

export type DiffViewerFoldElementProps = DivProps & {
  'data-hpd-diff-fold': '';
  'data-hidden-count': number;
};

export type DiffViewerSegmentElementProps = SpanProps & {
  'data-hpd-diff-segment': '';
  'data-changed'?: '';
};

export type DiffViewerSplitLineElementProps = DivProps & {
  'data-hpd-diff-split-line': '';
};

export type DiffViewerSplitSideElementProps = DivProps & {
  'data-hpd-diff-split-side': '';
  'data-side': 'left' | 'right';
  'data-line-type': DiffLine['type'] | 'empty';
};

export interface DiffViewerChildProps {
  model: DiffViewerModel;
  props: DiffViewerElementProps;
}

export interface DiffViewerFileChildProps {
  file: DiffFile;
  fileIndex: number;
  props: DiffViewerFileElementProps;
}

export interface DiffViewerHeaderChildProps {
  additions: number;
  deletions: number;
  displayName?: string;
  file: DiffFile;
  fileIndex: number;
  oldName?: string;
  newName?: string;
  props: DiffViewerHeaderElementProps;
  renamed: boolean;
}

export interface DiffViewerStatsChildProps {
  additions: number;
  deletions: number;
  file: DiffFile;
  fileIndex: number;
  props: DiffViewerStatsElementProps;
}

export interface DiffViewerContentChildProps {
  displayLines: DiffDisplayLine[];
  file: DiffFile;
  fileIndex: number;
  props: DiffViewerContentElementProps;
  remainingCount: number;
  splitPairs: DiffSplitLinePair[];
  truncated: boolean;
}

export interface DiffViewerLineChildProps {
  file: DiffFile;
  fileIndex: number;
  index: number;
  line: DiffLine;
  props: DiffViewerLineElementProps;
  segments: DiffSegment[] | null;
}

export interface DiffViewerFoldChildProps {
  file: DiffFile;
  fileIndex: number;
  fold: DiffFold;
  index: number;
  props: DiffViewerFoldElementProps;
}

export interface DiffViewerSplitLineChildProps {
  file: DiffFile;
  fileIndex: number;
  index: number;
  pair: DiffSplitLinePair;
  props: DiffViewerSplitLineElementProps;
}

export interface DiffViewerProps extends DivProps {
  child?: Snippet<[DiffViewerChildProps]>;
  children?: Snippet<[DiffViewerChildProps]>;
  contextLines?: number;
  file?: Snippet<[DiffViewerFileChildProps]>;
  fold?: Snippet<[DiffViewerFoldChildProps]>;
  header?: Snippet<[DiffViewerHeaderChildProps]>;
  line?: Snippet<[DiffViewerLineChildProps]>;
  maxLines?: number;
  newFile?: DiffFileInput;
  oldFile?: DiffFileInput;
  patch?: string;
  showHeader?: boolean;
  showLineNumbers?: boolean;
  showStats?: boolean;
  size?: DiffViewerSize;
  splitLine?: Snippet<[DiffViewerSplitLineChildProps]>;
  variant?: DiffViewerVariant;
  viewMode?: DiffViewerViewMode;
}

export interface DiffViewerFileProps extends DivProps {
  children?: Snippet<[DiffViewerFileChildProps]>;
  file?: DiffFile;
  fileIndex?: number;
}

export interface DiffViewerHeaderProps extends DivProps {
  children?: Snippet<[DiffViewerHeaderChildProps]>;
  file?: DiffFile;
  fileIndex?: number;
  showStats?: boolean;
}

export interface DiffViewerStatsProps extends SpanProps {
  children?: Snippet<[DiffViewerStatsChildProps]>;
  file?: DiffFile;
  fileIndex?: number;
}

export interface DiffViewerContentProps extends DivProps {
  children?: Snippet<[DiffViewerContentChildProps]>;
  file?: DiffFile;
  fileIndex?: number;
  fold?: Snippet<[DiffViewerFoldChildProps]>;
  line?: Snippet<[DiffViewerLineChildProps]>;
  splitLine?: Snippet<[DiffViewerSplitLineChildProps]>;
}

export interface DiffViewerLineProps extends DivProps {
  children?: Snippet<[DiffViewerLineChildProps]>;
  file?: DiffFile;
  fileIndex?: number;
  index?: number;
  line: DiffLine;
  segments?: DiffSegment[] | null;
  showLineNumbers?: boolean;
}

export interface DiffViewerSplitLineProps extends DivProps {
  children?: Snippet<[DiffViewerSplitLineChildProps]>;
  file?: DiffFile;
  fileIndex?: number;
  index?: number;
  pair: DiffSplitLinePair;
  showLineNumbers?: boolean;
}
