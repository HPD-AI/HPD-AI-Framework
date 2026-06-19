import type { ComposerTriggerMatch } from './types.js';

const WHITESPACE = /\s/u;

export function detectComposerTrigger(
  text: string,
  cursor: number,
  trigger: string,
): ComposerTriggerMatch | null {
  if (trigger.length === 0) {
    throw new Error('Composer trigger cannot be empty.');
  }

  const boundedCursor = Math.max(0, Math.min(cursor, text.length));
  const textBeforeCursor = text.slice(0, boundedCursor);

  for (let index = textBeforeCursor.length - 1; index >= 0; index -= 1) {
    const char = textBeforeCursor[index];
    if (char === undefined) continue;
    if (WHITESPACE.test(char)) return null;

    if (!textBeforeCursor.startsWith(trigger, index)) continue;
    const previous = index > 0 ? textBeforeCursor[index - 1] : undefined;
    if (previous !== undefined && !WHITESPACE.test(previous)) continue;

    return {
      cursor: boundedCursor,
      offset: index,
      query: textBeforeCursor.slice(index + trigger.length),
      trigger,
    };
  }

  return null;
}

export function getActiveComposerTrigger(
  text: string,
  cursor: number,
  triggers: readonly string[],
): ComposerTriggerMatch | null {
  for (const trigger of triggers) {
    const match = detectComposerTrigger(text, cursor, trigger);
    if (match) return match;
  }

  return null;
}
