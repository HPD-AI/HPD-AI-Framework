import type {
  CreateDirectiveTextPartsOptions,
  CreateMessageDirectiveOptions,
  DirectiveTextPart,
  MessageDirective,
} from './types.js';

export function createMessageDirective(
  options: CreateMessageDirectiveOptions,
): MessageDirective {
  return {
    id: options.id,
    label: options.label,
    metadata: options.metadata,
    text: options.text ?? `${options.trigger}${options.label}`,
    trigger: options.trigger,
    type: options.type,
  };
}

export function readMessageDirectives(
  message: { additionalProperties?: Record<string, unknown> } | null | undefined,
): MessageDirective[] {
  return readAdditionalPropertyDirectives(message?.additionalProperties);
}

export function readAdditionalPropertyDirectives(
  additionalProperties: Record<string, unknown> | null | undefined,
): MessageDirective[] {
  const raw = additionalProperties?.directives;
  if (!Array.isArray(raw)) return [];

  const directives: MessageDirective[] = [];
  for (const item of raw) {
    const directive = normalizeDirective(item);
    if (directive) directives.push(directive);
  }
  return directives;
}

export function createDirectiveTextParts(
  options: CreateDirectiveTextPartsOptions,
): DirectiveTextPart[] {
  const text = options.text;
  if (text.length === 0) return [];

  const directives = normalizeDirectives(options.directives ?? readMessageDirectives(options.message));
  if (directives.length === 0) {
    return [{ id: 'text:0', text, type: 'text' }];
  }

  const ranges = getDirectiveRanges(text, directives);
  if (ranges.length === 0) {
    return [{ id: 'text:0', text, type: 'text' }];
  }

  const parts: DirectiveTextPart[] = [];
  let cursor = 0;
  let textIndex = 0;
  let directiveIndex = 0;

  for (const range of ranges) {
    if (range.start > cursor) {
      parts.push({
        id: `text:${textIndex++}`,
        text: text.slice(cursor, range.start),
        type: 'text',
      });
    }

    parts.push({
      directive: range.directive,
      id: `directive:${directiveIndex++}:${range.directive.id}`,
      text: text.slice(range.start, range.end),
      type: 'directive',
    });
    cursor = range.end;
  }

  if (cursor < text.length) {
    parts.push({
      id: `text:${textIndex++}`,
      text: text.slice(cursor),
      type: 'text',
    });
  }

  return parts;
}

function normalizeDirectives(
  directives: readonly MessageDirective[],
): MessageDirective[] {
  const seen = new Set<string>();
  const normalized: MessageDirective[] = [];

  for (const directive of directives) {
    if (directive.text.length === 0) continue;

    const key = `${directive.trigger}:${directive.id}:${directive.text}`;
    if (seen.has(key)) continue;
    seen.add(key);
    normalized.push(directive);
  }

  return normalized.sort((a, b) => b.text.length - a.text.length);
}

interface DirectiveRange {
  directive: MessageDirective;
  end: number;
  start: number;
}

function getDirectiveRanges(text: string, directives: readonly MessageDirective[]): DirectiveRange[] {
  const ranges: DirectiveRange[] = [];

  for (const directive of directives) {
    let searchFrom = 0;
    while (searchFrom < text.length) {
      const start = text.indexOf(directive.text, searchFrom);
      if (start < 0) break;

      const end = start + directive.text.length;
      if (
        isDirectiveBoundary(text, start, end)
        && !ranges.some((range) => overlaps(start, end, range.start, range.end))
      ) {
        ranges.push({ directive, end, start });
      }

      searchFrom = end;
    }
  }

  return ranges.sort((a, b) => a.start - b.start);
}

function isDirectiveBoundary(text: string, start: number, end: number): boolean {
  const before = start > 0 ? text[start - 1] : '';
  const after = end < text.length ? text[end] : '';
  return !isWordCharacter(before) && !isWordCharacter(after);
}

function isWordCharacter(value: string): boolean {
  return /^[\p{L}\p{N}_-]$/u.test(value);
}

function overlaps(
  start: number,
  end: number,
  existingStart: number,
  existingEnd: number,
): boolean {
  return start < existingEnd && end > existingStart;
}

function normalizeDirective(value: unknown): MessageDirective | null {
  if (!isRecord(value)) return null;

  const id = readString(value.id);
  const label = readString(value.label);
  const trigger = readString(value.trigger);
  const type = readString(value.type);
  if (!id || !label || !trigger || !type) return null;

  const text = readString(value.text) ?? `${trigger}${label}`;
  return {
    id,
    label,
    metadata: isRecord(value.metadata) ? value.metadata : undefined,
    text,
    trigger,
    type,
  };
}

function readString(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
