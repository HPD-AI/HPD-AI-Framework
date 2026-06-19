export {
  detectComposerTrigger,
  getActiveComposerTrigger,
} from './trigger-detection.js';
export {
  createStaticComposerTriggerAdapter,
  getComposerTriggerCategories,
  getComposerTriggerItems,
} from './trigger-adapter.js';
export {
  applyComposerTriggerDirective,
  createComposerDirectiveAdditionalProperties,
  defaultComposerTriggerDirectiveFormatter,
  mergeComposerTriggerBehaviorResult,
} from './trigger-behavior.js';
export type {
  ComposerTriggerAdapter,
  ComposerTriggerApplyOptions,
  ComposerTriggerApplyResult,
  ComposerTriggerBehaviorKind,
  ComposerTriggerBehaviorResult,
  ComposerTriggerCategory,
  ComposerTriggerDirectiveFormatter,
  ComposerTriggerDirectiveFormatterOptions,
  ComposerTriggerItem,
  ComposerTriggerItemType,
  ComposerTriggerMatch,
  ComposerTriggerSelection,
} from './types.js';
