import type { RunConfig } from '@hpd-research/hpd-agent-client';

export type ComposerTriggerBehaviorKind = 'directive' | 'action';
export type ComposerTriggerItemType = 'mention' | 'command' | string;

export interface ComposerTriggerMatch {
  trigger: string;
  query: string;
  offset: number;
  cursor: number;
}

export interface ComposerTriggerCategory {
  id: string;
  label: string;
  description?: string;
  metadata?: Record<string, unknown>;
}

export interface ComposerTriggerItem {
  id: string;
  type: ComposerTriggerItemType;
  label: string;
  description?: string;
  categoryId?: string;
  metadata?: Record<string, unknown>;
}

export interface ComposerTriggerAdapter {
  categories?: () => readonly ComposerTriggerCategory[];
  categoryItems?: (categoryId: string) => readonly ComposerTriggerItem[];
  search?: (query: string) => readonly ComposerTriggerItem[];
  items?: () => readonly ComposerTriggerItem[];
}

export interface ComposerTriggerDirectiveFormatterOptions {
  trigger: string;
  item: ComposerTriggerItem;
}

export type ComposerTriggerDirectiveFormatter =
  (options: ComposerTriggerDirectiveFormatterOptions) => string;

export interface ComposerTriggerSelection {
  trigger: string;
  item: ComposerTriggerItem;
  match: ComposerTriggerMatch;
}

export interface ComposerTriggerApplyOptions {
  formatter?: ComposerTriggerDirectiveFormatter;
  removeOnExecute?: boolean;
  selection: ComposerTriggerSelection;
  text: string;
}

export interface ComposerTriggerApplyResult {
  additionalPropertiesPatch?: Record<string, unknown>;
  insertedText: string;
  item: ComposerTriggerItem;
  nextCursor: number;
  runConfigPatch?: RunConfig;
  text: string;
  trigger: string;
}

export interface ComposerTriggerBehaviorResult {
  additionalPropertiesPatch?: Record<string, unknown>;
  runConfigPatch?: RunConfig;
}
