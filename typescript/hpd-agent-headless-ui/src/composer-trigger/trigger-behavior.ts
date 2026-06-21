import type {
  ComposerTriggerApplyOptions,
  ComposerTriggerApplyResult,
  ComposerTriggerBehaviorResult,
  ComposerTriggerDirectiveFormatter,
  ComposerTriggerDirectiveFormatterOptions,
} from './types.js';
import { createMessageDirective } from '../directive-text/index.js';

export const defaultComposerTriggerDirectiveFormatter: ComposerTriggerDirectiveFormatter = ({
  trigger,
  item,
}: ComposerTriggerDirectiveFormatterOptions): string => {
  if (trigger === '/') return `/${item.id}`;
  if (trigger === '@') return `@${item.label}`;
  return `${trigger}${item.label}`;
};

export function createComposerDirectiveAdditionalProperties(
  options: ComposerTriggerDirectiveFormatterOptions,
): Record<string, unknown> {
  return {
    directives: [createMessageDirective({
      id: options.item.id,
      label: options.item.label,
      metadata: options.item.metadata,
      text: defaultComposerTriggerDirectiveFormatter(options),
      trigger: options.trigger,
      type: options.item.type,
    })],
  };
}

export function applyComposerTriggerDirective(
  options: ComposerTriggerApplyOptions,
): ComposerTriggerApplyResult {
  const { selection, text } = options;
  const formatter = options.formatter ?? defaultComposerTriggerDirectiveFormatter;
  const insertedText = options.removeOnExecute
    ? ''
    : formatter({ trigger: selection.trigger, item: selection.item });
  const before = text.slice(0, selection.match.offset);
  const after = text.slice(selection.match.cursor);
  const separator = insertedText.length > 0 && after.length > 0 && !after.startsWith(' ') ? ' ' : '';
  const nextText = `${before}${insertedText}${separator}${after}`;
  const nextCursor = before.length + insertedText.length + separator.length;

  return {
    insertedText,
    item: selection.item,
    nextCursor,
    text: nextText,
    trigger: selection.trigger,
  };
}

export function mergeComposerTriggerBehaviorResult(
  base: ComposerTriggerApplyResult,
  result: ComposerTriggerBehaviorResult | void,
): ComposerTriggerApplyResult {
  if (!result) return base;

  return {
    ...base,
    additionalPropertiesPatch: result.additionalPropertiesPatch,
    runConfigPatch: result.runConfigPatch,
  };
}
